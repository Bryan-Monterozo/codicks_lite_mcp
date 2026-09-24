using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Execution;

public sealed class HostProcessExecutionService(
    IWorkspaceResolver workspaceResolver,
    IWorkspacePathPolicy pathPolicy,
    IExecutablePolicy executablePolicy) : IProcessExecutionService
{
    private static readonly string[] SensitiveEnvironmentNameFragments =
    [
        "TOKEN",
        "SECRET",
        "PASSWORD",
        "CREDENTIAL",
        "API_KEY",
        "APIKEY",
        "PRIVATE_KEY"
    ];

    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExecutionMode != ExecutionMode.Host)
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.UnsupportedExecutionMode,
                $"Host execution service cannot run mode '{request.ExecutionMode}'.");
        }

        var resolution = workspaceResolver.Resolve(request.WorkspaceId);
        if (!resolution.Resolved || resolution.Workspace is null)
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.WorkspaceNotFound,
                resolution.Message);
        }

        var decision = executablePolicy.Evaluate(
            resolution.Workspace,
            request);

        if (!decision.Allowed)
        {
            throw new ProcessExecutionException(
                decision.Error,
                decision.Message);
        }

        var path = pathPolicy.ValidateExisting(
            request.WorkspaceId,
            request.RelativeWorkingDirectory,
            WorkspaceOperation.Execute,
            allowWorkspaceRoot: true);

        if (!path.Allowed ||
            path.FullPath is null ||
            path.NormalizedRelativePath is null ||
            path.EntryKind != FileSystemEntryKind.Directory)
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.InvalidWorkingDirectory,
                path.Allowed
                    ? "The execution working directory must be an existing directory."
                    : path.Message);
        }

        var startInfo = CreateStartInfo(
            request,
            path.FullPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new ProcessExecutionException(
                    ProcessExecutionError.ProcessStartFailed,
                    $"Failed to start executable '{request.Executable}'.");
            }
        }
        catch (ProcessExecutionException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is Win32Exception or
                  InvalidOperationException or
                  IOException)
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.ProcessStartFailed,
                $"Failed to start executable '{request.Executable}': {exception.Message}",
                exception);
        }

        var stdoutTask = ReadBoundedAsync(
            process.StandardOutput,
            decision.EffectiveMaxOutputBytes);

        var stderrTask = ReadBoundedAsync(
            process.StandardError,
            decision.EffectiveMaxOutputBytes);

        var timedOut = false;
        var cancelled = false;

        using var timeoutSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(
                    decision.EffectiveTimeoutSeconds));

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(
                linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut =
                !cancelled &&
                timeoutSource.IsCancellationRequested;

            TryKillProcessTree(process);

            try
            {
                await process.WaitForExitAsync(
                    CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // Process already terminated between cancellation and wait.
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        stopwatch.Stop();

        return new ProcessExecutionResult(
            request.WorkspaceId,
            path.NormalizedRelativePath,
            request.Executable,
            process.HasExited
                ? process.ExitCode
                : null,
            stdout.Text,
            stderr.Text,
            timedOut,
            cancelled,
            stopwatch.ElapsedMilliseconds,
            stdout.Truncated,
            stderr.Truncated);
    }

    private static ProcessStartInfo CreateStartInfo(
        ProcessExecutionRequest request,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        RemoveSensitiveEnvironmentVariables(
            startInfo);

        return startInfo;
    }

    private static void RemoveSensitiveEnvironmentVariables(
        ProcessStartInfo startInfo)
    {
        var sensitiveKeys = startInfo.Environment.Keys
            .Where(IsSensitiveEnvironmentName)
            .ToArray();

        foreach (var key in sensitiveKeys)
        {
            startInfo.Environment.Remove(key);
        }
    }

    private static bool IsSensitiveEnvironmentName(
        string name) =>
        SensitiveEnvironmentNameFragments.Any(
            fragment => name.Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase));

    private static async Task<BoundedOutput> ReadBoundedAsync(
        StreamReader reader,
        int maxBytes)
    {
        var builder = new StringBuilder();
        var buffer = new char[4_096];
        var retainedBytes = 0;
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(
                buffer.AsMemory(),
                CancellationToken.None);

            if (read == 0)
            {
                break;
            }

            if (truncated)
            {
                continue;
            }

            var chunk = buffer.AsSpan(0, read);
            var chunkBytes =
                Encoding.UTF8.GetByteCount(chunk);

            if (retainedBytes + chunkBytes <= maxBytes)
            {
                builder.Append(chunk);
                retainedBytes += chunkBytes;
                continue;
            }

            var remainingBytes =
                maxBytes - retainedBytes;

            AppendUtf8Prefix(
                builder,
                chunk,
                remainingBytes);

            truncated = true;
        }

        return new BoundedOutput(
            builder.ToString(),
            truncated);
    }

    private static void AppendUtf8Prefix(
        StringBuilder builder,
        ReadOnlySpan<char> value,
        int maxBytes)
    {
        if (maxBytes <= 0 ||
            value.Length == 0)
        {
            return;
        }

        var length = 0;
        var retainedBytes = 0;

        while (length < value.Length)
        {
            var charCount =
                char.IsHighSurrogate(value[length]) &&
                length + 1 < value.Length &&
                char.IsLowSurrogate(value[length + 1])
                    ? 2
                    : 1;

            var bytes = Encoding.UTF8.GetByteCount(
                value.Slice(
                    length,
                    charCount));

            if (retainedBytes + bytes > maxBytes)
            {
                break;
            }

            retainedBytes += bytes;
            length += charCount;
        }

        if (length > 0)
        {
            builder.Append(
                value[..length]);
        }
    }

    private static void TryKillProcessTree(
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
            when (exception is InvalidOperationException or
                  NotSupportedException or
                  Win32Exception)
        {
            // Best effort: the process may have exited between checks.
        }
    }

    private sealed record BoundedOutput(
        string Text,
        bool Truncated);
}
