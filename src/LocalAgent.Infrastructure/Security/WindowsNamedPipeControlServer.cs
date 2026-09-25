using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using Microsoft.Extensions.Hosting;

namespace LocalAgent.Infrastructure.Security;

public sealed class WindowsNamedPipeControlServer(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver,
    LocalSessionControlProcessor processor) : BackgroundService
{
    private const int MaxServerInstances =
        4;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var stateDirectory =
            pathResolver.Resolve(
                configuration.Agent.StateDirectory);

        var pipeName =
            CreatePipeName(
                stateDirectory);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe =
                CreateServer(
                    pipeName);

            try
            {
                await pipe.WaitForConnectionAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!WindowsNamedPipeClientLocality.IsLocalClient(
                    pipe.SafePipeHandle))
            {
                await WriteResponseAsync(
                    pipe,
                    LocalSessionControlProcessor.CreateTransportError(
                        "Remote session control is not allowed."),
                    stoppingToken);

                continue;
            }

            await HandleClientAsync(
                pipe,
                stoppingToken);
        }
    }

    public static string CreatePipeName(
        string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            stateDirectory);

        var normalized =
            Path.GetFullPath(
                    stateDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalized));

        return
            "CodicksLiteMcp.Control." +
            Convert.ToHexString(hash)[..24];
    }

    private static NamedPipeServerStream CreateServer(
        string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            MaxServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous |
            PipeOptions.CurrentUserOnly);

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
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

    private static async Task WriteResponseAsync(
        NamedPipeServerStream pipe,
        string response,
        CancellationToken cancellationToken)
    {
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
            response.AsMemory(),
            cancellationToken);
    }
}
