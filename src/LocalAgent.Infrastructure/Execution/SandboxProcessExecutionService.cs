using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Execution;

public sealed class SandboxProcessExecutionService(
    AgentConfiguration configuration,
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

    private readonly SandboxExecutionOptions _sandbox =
        configuration.Execution.Sandbox;

    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExecutionMode != ExecutionMode.Sandbox)
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.UnsupportedExecutionMode,
                $"Sandbox execution service cannot run mode '{request.ExecutionMode}'.");
        }

        if (!OperatingSystem.IsMacOS())
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.SandboxUnavailable,
                "The configured sandbox backend requires macOS.");
        }

        var resolution =
            workspaceResolver.Resolve(
                request.WorkspaceId);

        if (!resolution.Resolved ||
            resolution.Workspace is null)
        {
            throw new ProcessExecutionException(
                ProcessExecutionError.WorkspaceNotFound,
                resolution.Message);
        }

        var decision =
            executablePolicy.Evaluate(
                resolution.Workspace,
                request);

        if (!decision.Allowed)
        {
            throw new ProcessExecutionException(
                decision.Error,
                decision.Message);
        }

        var path =
            pathPolicy.ValidateExisting(
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
                    ? "The sandbox working directory must be an existing directory."
                    : path.Message);
        }

        var containerName =
            $"codicks-{Guid.NewGuid():N}";

        var startInfo =
            CreateStartInfo(
                request,
                resolution.Workspace,
                path.NormalizedRelativePath,
                containerName);

        using var process =
            new Process
            {
                StartInfo = startInfo
            };

        var stopwatch =
            Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw SandboxUnavailable(
                    "The sandbox runtime did not start.");
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
            throw SandboxUnavailable(
                $"Unable to start sandbox runtime '{_sandbox.RuntimeExecutable}': {exception.Message}",
                exception);
        }

        var stdoutTask =
            ReadBoundedAsync(
                process.StandardOutput,
                decision.EffectiveMaxOutputBytes);

        var stderrTask =
            ReadBoundedAsync(
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
            cancelled =
                cancellationToken.IsCancellationRequested;

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
                // Process already exited.
            }

            await TryDeleteContainerAsync(
                containerName);
        }

        var stdout =
            await stdoutTask;

        var stderr =
            await stderrTask;

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

    private ProcessStartInfo CreateStartInfo(
        ProcessExecutionRequest request,
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string containerName)
    {
        var startInfo =
            CreateRuntimeStartInfo();

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--rm");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(containerName);
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("none");
        startInfo.ArgumentList.Add("--cpus");
        startInfo.ArgumentList.Add(
            _sandbox.CpuCount.ToString(
                CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--memory");
        startInfo.ArgumentList.Add(
            $"{_sandbox.MemoryMegabytes}M");

        if (_sandbox.ReadOnlyRoot)
        {
            startInfo.ArgumentList.Add(
                "--read-only");
        }

        if (!_sandbox.NetworkEnabled)
        {
            startInfo.ArgumentList.Add(
                "--network");
            startInfo.ArgumentList.Add(
                "none");
            startInfo.ArgumentList.Add(
                "--no-dns");
        }

        startInfo.ArgumentList.Add(
            "--tmpfs");
        startInfo.ArgumentList.Add(
            $"/tmp:size={_sandbox.TmpfsMegabytes}M,mode=1777");

        startInfo.ArgumentList.Add(
            "--volume");
        startInfo.ArgumentList.Add(
            $"{workspace.Root}:/workspace");

        startInfo.ArgumentList.Add(
            "--workdir");
        startInfo.ArgumentList.Add(
            ToContainerWorkingDirectory(
                normalizedRelativePath));

        startInfo.ArgumentList.Add(
            "--env");
        startInfo.ArgumentList.Add(
            "HOME=/tmp");

        startInfo.ArgumentList.Add(
            "--env");
        startInfo.ArgumentList.Add(
            "DOTNET_CLI_HOME=/tmp/dotnet-home");

        startInfo.ArgumentList.Add(
            _sandbox.Image);

        startInfo.ArgumentList.Add(
            request.Executable);

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        return startInfo;
    }

    private ProcessStartInfo CreateRuntimeStartInfo()
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName = _sandbox.RuntimeExecutable,
                WorkingDirectory =
                    Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

        RemoveSensitiveEnvironmentVariables(
            startInfo);

        return startInfo;
    }

    private static string ToContainerWorkingDirectory(
        string normalizedRelativePath)
    {
        if (string.IsNullOrEmpty(
                normalizedRelativePath))
        {
            return "/workspace";
        }

        return "/workspace/" +
            normalizedRelativePath.Replace(
                Path.DirectorySeparatorChar,
                '/');
    }

    private async Task TryDeleteContainerAsync(
        string containerName)
    {
        using var cleanup =
            new Process
            {
                StartInfo =
                    CreateRuntimeStartInfo()
            };

        cleanup.StartInfo.ArgumentList.Add(
            "delete");
        cleanup.StartInfo.ArgumentList.Add(
            "--force");
        cleanup.StartInfo.ArgumentList.Add(
            containerName);

        try
        {
            if (!cleanup.Start())
            {
                return;
            }

            var stdoutTask =
                cleanup.StandardOutput.ReadToEndAsync();

            var stderrTask =
                cleanup.StandardError.ReadToEndAsync();

            using var timeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));

            try
            {
                await cleanup.WaitForExitAsync(
                    timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(cleanup);
            }

            _ = await stdoutTask;
            _ = await stderrTask;
        }
        catch (Exception exception)
            when (exception is Win32Exception or
                  InvalidOperationException or
                  IOException)
        {
            // Cleanup is best effort.
        }
    }

    private static void RemoveSensitiveEnvironmentVariables(
        ProcessStartInfo startInfo)
    {
        var sensitiveKeys =
            startInfo.Environment.Keys
                .Where(
                    IsSensitiveEnvironmentName)
                .ToArray();

        foreach (var key in sensitiveKeys)
        {
            startInfo.Environment.Remove(
                key);
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
        var builder =
            new StringBuilder();

        var buffer =
            new char[4_096];

        var retainedBytes = 0;
        var truncated = false;

        while (true)
        {
            var read =
                await reader.ReadAsync(
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

            var chunk =
                buffer.AsSpan(
                    0,
                    read);

            var chunkBytes =
                Encoding.UTF8.GetByteCount(
                    chunk);

            if (retainedBytes + chunkBytes <= maxBytes)
            {
                builder.Append(
                    chunk);

                retainedBytes +=
                    chunkBytes;

                continue;
            }

            AppendUtf8Prefix(
                builder,
                chunk,
                maxBytes - retainedBytes);

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
                char.IsHighSurrogate(
                    value[length]) &&
                length + 1 < value.Length &&
                char.IsLowSurrogate(
                    value[length + 1])
                    ? 2
                    : 1;

            var bytes =
                Encoding.UTF8.GetByteCount(
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
            // Best effort.
        }
    }

    private static ProcessExecutionException SandboxUnavailable(
        string message,
        Exception? innerException = null) =>
        new(
            ProcessExecutionError.SandboxUnavailable,
            message,
            innerException);

    private sealed record BoundedOutput(
        string Text,
        bool Truncated);
}
