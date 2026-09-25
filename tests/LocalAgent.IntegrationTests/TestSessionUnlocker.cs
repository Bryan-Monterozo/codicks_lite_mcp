using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LocalAgent.Infrastructure.Security;

namespace LocalAgent.IntegrationTests;

internal sealed class TestSessionUnlocker
{
    private readonly TaskCompletionSource<string> _otpSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Action<string> StandardErrorLines =>
        CaptureStandardErrorLine;

    public static string CreateShortRoot(
        string prefix)
    {
        var baseDirectory =
            OperatingSystem.IsWindows()
                ? Path.GetTempPath()
                : "/tmp";

        return Path.Combine(
            baseDirectory,
            $"cdx-{prefix}-{Guid.NewGuid():N}"[..17]);
    }

    public Task UnlockFullAsync(
        string stateRoot,
        CancellationToken cancellationToken = default) =>
        UnlockAsync(
            stateRoot,
            "Full",
            cancellationToken);

    public Task UnlockReadOnlyAsync(
        string stateRoot,
        CancellationToken cancellationToken = default) =>
        UnlockAsync(
            stateRoot,
            "ReadOnly",
            cancellationToken);

    private async Task UnlockAsync(
        string stateRoot,
        string mode,
        CancellationToken cancellationToken)
    {
        var otp =
            await _otpSource.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

        if (OperatingSystem.IsMacOS())
        {
            await UnlockUnixSocketAsync(
                stateRoot,
                mode,
                otp,
                cancellationToken);

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            await UnlockNamedPipeAsync(
                stateRoot,
                mode,
                otp,
                cancellationToken);

            return;
        }

        throw new PlatformNotSupportedException(
            "Codicks Lite integration session control supports macOS and Windows.");
    }

    private static async Task UnlockUnixSocketAsync(
        string stateRoot,
        string mode,
        string otp,
        CancellationToken cancellationToken)
    {
        var socketPath =
            Path.Combine(
                stateRoot,
                "run",
                "control.sock");

        await WaitForSocketAsync(
            socketPath,
            cancellationToken);

        using var socket =
            new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified);

        await socket.ConnectAsync(
            new UnixDomainSocketEndPoint(
                socketPath),
            cancellationToken);

        await using var stream =
            new NetworkStream(
                socket,
                ownsSocket: false);

        await SendUnlockRequestAsync(
            stream,
            mode,
            otp,
            cancellationToken);
    }

    private static async Task UnlockNamedPipeAsync(
        string stateRoot,
        string mode,
        string otp,
        CancellationToken cancellationToken)
    {
        var pipeName =
            WindowsNamedPipeControlServer.CreatePipeName(
                stateRoot);

        await using var pipe =
            new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous |
                PipeOptions.CurrentUserOnly);

        var connectTask =
            pipe.ConnectAsync(
                cancellationToken);

        await connectTask.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);

        await SendUnlockRequestAsync(
            pipe,
            mode,
            otp,
            cancellationToken);
    }

    private static async Task SendUnlockRequestAsync(
        Stream stream,
        string mode,
        string otp,
        CancellationToken cancellationToken)
    {
        using var reader =
            new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4_096,
                leaveOpen: true);

        await using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4_096,
                leaveOpen: true)
            {
                AutoFlush = true
            };

        var request =
            JsonSerializer.Serialize(
                new
                {
                    action =
                        "unlock",
                    mode,
                    otp,
                    leaseMinutes =
                        30
                });

        await writer.WriteLineAsync(
            request.AsMemory(),
            cancellationToken);

        var responseLine =
            await reader.ReadLineAsync(
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                responseLine))
        {
            throw new InvalidOperationException(
                "Codicks Lite local control transport returned no unlock response.");
        }

        using var response =
            JsonDocument.Parse(
                responseLine);

        if (!response.RootElement.TryGetProperty(
                "success",
                out var success) ||
            success.ValueKind !=
                JsonValueKind.True)
        {
            throw new InvalidOperationException(
                $"Codicks Lite test session unlock failed: {responseLine}");
        }
    }

    private void CaptureStandardErrorLine(
        string line)
    {
        const string marker =
            "Local OTP:";

        var markerIndex =
            line.IndexOf(
                marker,
                StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return;
        }

        var remaining =
            line.AsSpan(
                markerIndex +
                marker.Length);

        var start =
            0;

        while (start < remaining.Length &&
               char.IsWhiteSpace(
                   remaining[start]))
        {
            start++;
        }

        var length =
            0;

        while (start + length <
                   remaining.Length &&
               char.IsAsciiDigit(
                   remaining[
                       start +
                       length]))
        {
            length++;
        }

        if (length > 0)
        {
            _otpSource.TrySetResult(
                new string(
                    remaining.Slice(
                        start,
                        length)));
        }
    }

    private static async Task WaitForSocketAsync(
        string socketPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0;
             attempt < 100;
             attempt++)
        {
            if (File.Exists(
                    socketPath))
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    25),
                cancellationToken);
        }

        throw new TimeoutException(
            $"Codicks Lite test control socket was not created: {socketPath}");
    }
}
