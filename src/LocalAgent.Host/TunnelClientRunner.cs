using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LocalAgent.Host;

public interface ITunnelClientRunner
{
    Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string runtimeApiKey,
        CancellationToken cancellationToken = default);
}

public sealed class TunnelClientRunner :
    ITunnelClientRunner
{
    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string runtimeApiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            arguments);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            runtimeApiKey);

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    "tunnel-client",
                RedirectStandardOutput =
                    true,
                RedirectStandardError =
                    true,
                RedirectStandardInput =
                    false,
                UseShellExecute =
                    false,
                CreateNoWindow =
                    true,
                StandardOutputEncoding =
                    Encoding.UTF8,
                StandardErrorEncoding =
                    Encoding.UTF8
            };

        foreach (var argument in
                 arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        startInfo.Environment[
            "CONTROL_PLANE_API_KEY"] =
            runtimeApiKey;

        using var process =
            new Process
            {
                StartInfo =
                    startInfo
            };

        try
        {
            if (!process.Start())
            {
                throw new IOException(
                    "tunnel-client did not start.");
            }
        }
        catch (Exception exception)
            when (exception is
                Win32Exception or
                InvalidOperationException)
        {
            throw new IOException(
                "Unable to start tunnel-client.",
                exception);
        }

        var stdoutTask =
            PumpAsync(
                process.StandardOutput,
                Console.Out,
                cancellationToken);

        var stderrTask =
            PumpAsync(
                process.StandardError,
                Console.Error,
                cancellationToken);

        try
        {
            await process.WaitForExitAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(
                process);

            throw;
        }

        await Task.WhenAll(
            stdoutTask,
            stderrTask);

        return process.ExitCode;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line =
                await reader.ReadLineAsync(
                    cancellationToken);

            if (line is null)
            {
                return;
            }

            await writer.WriteLineAsync(
                line.AsMemory(),
                cancellationToken);
        }
    }

    private static void TryKill(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                NotSupportedException or
                Win32Exception)
        {
        }
    }
}
