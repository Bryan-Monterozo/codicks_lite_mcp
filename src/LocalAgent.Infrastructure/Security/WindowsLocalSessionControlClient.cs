using System.IO.Pipes;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.Security;

public sealed class WindowsLocalSessionControlClient(
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

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The Windows local session-control client requires Windows.");
        }

        var stateDirectory =
            pathResolver.Resolve(
                configuration.Agent.StateDirectory);

        var pipeName =
            WindowsNamedPipeControlServer.CreatePipeName(
                stateDirectory);

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
                "Local session-control named pipe returned no response.");
        }

        return LocalSessionControlProcessor.DeserializeResponse(
            response);
    }
}
