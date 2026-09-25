using System.Net.Sockets;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using Microsoft.Extensions.Hosting;

namespace LocalAgent.Infrastructure.Security;

public sealed class UnixControlSocketServer(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver,
    LocalSessionControlProcessor processor) : BackgroundService
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

        var runDirectory =
            Path.Combine(
                pathResolver.Resolve(
                    configuration.Agent.StateDirectory),
                "run");

        Directory.CreateDirectory(
            runDirectory);

        File.SetUnixFileMode(
            runDirectory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        _socketPath =
            Path.Combine(
                runDirectory,
                "control.sock");

        DeleteStaleSocket(
            _socketPath);

        _listener =
            new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified);

        _listener.Bind(
            new UnixDomainSocketEndPoint(
                _socketPath));

        File.SetUnixFileMode(
            _socketPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);

        _listener.Listen(
            backlog: 4);

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket client;

            try
            {
                client =
                    await _listener.AcceptAsync(
                        stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await HandleClientAsync(
                client,
                stoppingToken);
        }
    }

    public override Task StopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            _listener?.Dispose();
        }
        finally
        {
            if (_socketPath is not null)
            {
                DeleteStaleSocket(
                    _socketPath);
            }
        }

        return base.StopAsync(
            cancellationToken);
    }

    private async Task HandleClientAsync(
        Socket client,
        CancellationToken cancellationToken)
    {
        using (client)
        using (var stream =
               new NetworkStream(
                   client,
                   ownsSocket: false))
        using (var reader =
               new StreamReader(
                   stream,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 4_096,
                   leaveOpen: true))
        await using (var writer =
                     new StreamWriter(
                         stream,
                         new UTF8Encoding(
                             encoderShouldEmitUTF8Identifier: false),
                         bufferSize: 4_096,
                         leaveOpen: true)
                     {
                         AutoFlush = true
                     })
        {
            var line =
                await reader.ReadLineAsync(
                    cancellationToken);

            var response =
                processor.Process(
                    line);

            await writer.WriteLineAsync(
                response.AsMemory(),
                cancellationToken);
        }
    }

    private static void DeleteStaleSocket(
        string socketPath)
    {
        try
        {
            File.Delete(
                socketPath);
        }
        catch (FileNotFoundException)
        {
        }
    }
}
