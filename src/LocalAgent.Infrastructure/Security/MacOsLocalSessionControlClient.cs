using System.Net.Sockets;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.Security;

public sealed class MacOsLocalSessionControlClient(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver) :
    ILocalSessionControlClient
{
    public async Task<LocalSessionControlResponse> SendAsync(
        LocalSessionControlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The macOS local session-control client requires macOS.");
        }

        var socketPath =
            Path.Combine(
                pathResolver.Resolve(
                    configuration.Agent.StateDirectory),
                "run",
                "control.sock");

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

        return await SendCoreAsync(
            stream,
            request,
            cancellationToken);
    }

    private static async Task<LocalSessionControlResponse> SendCoreAsync(
        Stream stream,
        LocalSessionControlRequest request,
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

        await writer.WriteLineAsync(
            LocalSessionControlProcessor.SerializeRequest(
                    request)
                .AsMemory(),
            cancellationToken);

        var response =
            await reader.ReadLineAsync(
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                response))
        {
            throw new IOException(
                "Local session-control socket returned no response.");
        }

        return LocalSessionControlProcessor.DeserializeResponse(
            response);
    }
}
