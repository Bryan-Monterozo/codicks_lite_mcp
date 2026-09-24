using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using Microsoft.Extensions.Hosting;

namespace LocalAgent.Infrastructure.Security;

public sealed class UnixControlSocketServer(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver,
    ISessionGuard sessionGuard) : BackgroundService
{
    private Socket? _listener;
    private string? _socketPath;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var runDirectory = Path.Combine(
            pathResolver.Resolve(configuration.Agent.StateDirectory),
            "run");
        Directory.CreateDirectory(runDirectory);
        File.SetUnixFileMode(
            runDirectory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        _socketPath = Path.Combine(runDirectory, "control.sock");
        DeleteStaleSocket(_socketPath);

        _listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);

        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        File.SetUnixFileMode(
            _socketPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);

        _listener.Listen(backlog: 4);

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await HandleClientAsync(client, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener?.Dispose();
        }
        finally
        {
            if (_socketPath is not null)
            {
                DeleteStaleSocket(_socketPath);
            }
        }

        return base.StopAsync(cancellationToken);
    }

    private async Task HandleClientAsync(
        Socket client,
        CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = new NetworkStream(client, ownsSocket: false))
        using (var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4_096,
            leaveOpen: true))
        using (var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4_096,
            leaveOpen: true)
        {
            AutoFlush = true
        })
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                await WriteResponseAsync(
                    writer,
                    new ControlResponse(false, "Invalid request.", null),
                    cancellationToken);
                return;
            }

            ControlRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ControlRequest>(
                    line,
                    JsonOptions);
            }
            catch (JsonException)
            {
                request = null;
            }

            if (request is null)
            {
                await WriteResponseAsync(
                    writer,
                    new ControlResponse(false, "Invalid request.", null),
                    cancellationToken);
                return;
            }

            var response = ProcessRequest(request);
            await WriteResponseAsync(
                writer,
                response,
                cancellationToken);
        }
    }

    private ControlResponse ProcessRequest(ControlRequest request)
    {
        if (string.Equals(
                request.Action,
                "status",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ControlResponse(
                true,
                null,
                sessionGuard.GetStatus());
        }

        if (string.Equals(
                request.Action,
                "lock",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ControlResponse(
                true,
                null,
                sessionGuard.Lock());
        }

        if (!string.Equals(
                request.Action,
                "unlock",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ControlResponse(
                false,
                "Unknown action.",
                null);
        }

        if (!Enum.TryParse<AgentAccessMode>(
                request.Mode,
                ignoreCase: true,
                out var mode))
        {
            return new ControlResponse(
                false,
                "Mode must be ReadOnly or Full.",
                null);
        }

        var success = sessionGuard.TryUnlock(
            mode,
            request.Otp ?? string.Empty,
            request.LeaseMinutes,
            out var errorMessage);

        return new ControlResponse(
            success,
            errorMessage,
            sessionGuard.GetStatus());
    }

    private static Task WriteResponseAsync(
        StreamWriter writer,
        ControlResponse response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        return writer.WriteLineAsync(
            json.AsMemory(),
            cancellationToken);
    }

    private static void DeleteStaleSocket(string socketPath)
    {
        try
        {
            File.Delete(socketPath);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record ControlRequest(
        string Action,
        string? Mode,
        string? Otp,
        int? LeaseMinutes);

    private sealed record ControlResponse(
        bool Success,
        string? Error,
        SessionStatus? Status);
}
