using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WindowsNamedPipeControlServerTests : IDisposable
{
    private readonly string _stateRoot =
        Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-win-pipe-{Guid.NewGuid():N}");

    [Fact]
    public async Task NamedPipe_RoundTripsSessionControl_AndSerializesConcurrentRequests()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(
            _stateRoot);

        var configuration =
            CreateConfiguration();

        var guard =
            new SessionGuard(
                configuration,
                new SequenceOtpGenerator(
                    "123456",
                    "654321"),
                TimeProvider.System);

        using var server =
            new WindowsNamedPipeControlServer(
                configuration,
                new UserPathResolver(),
                new LocalSessionControlProcessor(
                    guard));

        await server.StartAsync(
            CancellationToken.None);

        try
        {
            var pipeName =
                WindowsNamedPipeControlServer.CreatePipeName(
                    _stateRoot);

            var status =
                await SendAsync(
                    pipeName,
                    """{"action":"status"}""");

            AssertResponse(
                status,
                success: true,
                expectedMode: "Locked");

            Assert.DoesNotContain(
                "123456",
                status,
                StringComparison.Ordinal);

            var invalid =
                await SendAsync(
                    pipeName,
                    """
                    {"action":"unlock","mode":"Full","otp":"999999","leaseMinutes":5}
                    """);

            AssertResponse(
                invalid,
                success: false,
                expectedMode: "Locked");

            var readOnly =
                await SendAsync(
                    pipeName,
                    """
                    {"action":"unlock","mode":"ReadOnly","otp":"123456","leaseMinutes":5}
                    """);

            AssertResponse(
                readOnly,
                success: true,
                expectedMode: "ReadOnly");

            Assert.True(
                guard.AuthorizeRead()
                    .Allowed);

            Assert.False(
                guard.AuthorizeMutation()
                    .Allowed);

            var locked =
                await SendAsync(
                    pipeName,
                    """{"action":"lock"}""");

            AssertResponse(
                locked,
                success: true,
                expectedMode: "Locked");

            var full =
                await SendAsync(
                    pipeName,
                    """
                    {"action":"unlock","mode":"Full","otp":"654321","leaseMinutes":5}
                    """);

            AssertResponse(
                full,
                success: true,
                expectedMode: "Full");

            Assert.True(
                guard.AuthorizeMutation()
                    .Allowed);

            var parallel =
                await Task.WhenAll(
                    Enumerable
                        .Range(
                            0,
                            4)
                        .Select(
                            _ =>
                                SendAsync(
                                    pipeName,
                                    """{"action":"status"}""")));

            Assert.All(
                parallel,
                response =>
                    AssertResponse(
                        response,
                        success: true,
                        expectedMode: "Full"));
        }
        finally
        {
            await server.StopAsync(
                CancellationToken.None);
        }
    }

    [Fact]
    public void PipeName_IsStablePerStateDirectory_AndDoesNotExposePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var first =
            WindowsNamedPipeControlServer.CreatePipeName(
                _stateRoot);

        var same =
            WindowsNamedPipeControlServer.CreatePipeName(
                _stateRoot);

        var other =
            WindowsNamedPipeControlServer.CreatePipeName(
                _stateRoot + "-other");

        Assert.Equal(
            first,
            same);

        Assert.NotEqual(
            first,
            other);

        Assert.StartsWith(
            "CodicksLiteMcp.Control.",
            first,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            _stateRoot,
            first,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SendAsync(
        string pipeName,
        string request)
    {
        await using var pipe =
            new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous |
                PipeOptions.CurrentUserOnly);

        var connectTask =
            pipe.ConnectAsync(
                CancellationToken.None);

        await connectTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        using var reader =
            new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4_096,
                leaveOpen: true);

        await using var writer =
            new StreamWriter(
                pipe,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4_096,
                leaveOpen: true)
            {
                AutoFlush = true
            };

        await writer.WriteLineAsync(
            request);

        var response =
            await reader.ReadLineAsync();

        Assert.False(
            string.IsNullOrWhiteSpace(
                response));

        return response!;
    }

    private static void AssertResponse(
        string response,
        bool success,
        string expectedMode)
    {
        using var json =
            JsonDocument.Parse(
                response);

        Assert.Equal(
            success,
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.Equal(
            expectedMode,
            json.RootElement
                .GetProperty(
                    "status")
                .GetProperty(
                    "mode")
                .GetString());
    }

    private AgentConfiguration CreateConfiguration() =>
        new()
        {
            Agent =
            {
                StateDirectory =
                    _stateRoot
            },
            SessionSecurity =
                new SessionSecurityOptions
                {
                    DefaultMode =
                        AgentAccessMode.Locked,
                    DefaultLeaseMinutes =
                        30,
                    OtpDigits =
                        6,
                    OtpLifetimeMinutes =
                        10
                }
        };

    private sealed class SequenceOtpGenerator(
        params string[] values) :
        ISessionOtpGenerator
    {
        private readonly Queue<string> _values =
            new(
                values);

        public string Generate(
            int digits)
        {
            Assert.True(
                _values.Count > 0);

            var value =
                _values.Dequeue();

            Assert.Equal(
                digits,
                value.Length);

            return value;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(
                    _stateRoot))
            {
                Directory.Delete(
                    _stateRoot,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
