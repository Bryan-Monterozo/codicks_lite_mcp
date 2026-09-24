using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalAgent.Core.Audit;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Security;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class ExecutionTools
{
    [McpServerTool(
        Name = "process_exec",
        Title = "Execute approved workspace process",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Executes one configured executable directly inside an approved workspace. Requires FULL session authorization and workspace Execute permission. No shell command string is used.")]
    public static async Task<ProcessExecutionResult> ProcessExec(
        IProcessExecutionService executionService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Configured executable command name, such as dotnet or git. Absolute executable paths are not allowed.")] string executable,
        [Description("Arguments passed directly through ProcessStartInfo.ArgumentList.")] string[] arguments,
        [Description("Workspace-relative existing directory. Use an empty string for the workspace root.")] string relativeWorkingDirectory = "",
        [Description("Optional timeout in seconds, bounded by execution policy.")] int? timeoutSeconds = null,
        [Description("Optional stdout/stderr retention limit in bytes per stream, bounded by execution policy.")] int? maxOutputBytes = null,
        [Description("Execution mode: 'Host' for native macOS execution or 'Sandbox' for the configured isolated container backend.")] string executionMode = "Host",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(sessionGuard);
        ArgumentNullException.ThrowIfNull(arguments);

        var stopwatch = Stopwatch.StartNew();

        var authorization =
            sessionGuard.AuthorizeFullAccess();

        if (!authorization.Allowed)
        {
            stopwatch.Stop();

            WriteFailureAudit(
                auditWriter,
                workspaceId,
                executable,
                arguments.Length,
                executionMode,
                stopwatch.ElapsedMilliseconds,
                authorization.ErrorCode);

            throw new McpException(
                $"{authorization.ErrorCode}: {authorization.Message}");
        }

        if (!Enum.TryParse<ExecutionMode>(
                executionMode,
                ignoreCase: true,
                out var parsedExecutionMode))
        {
            stopwatch.Stop();

            WriteFailureAudit(
                auditWriter,
                workspaceId,
                executable,
                arguments.Length,
                executionMode,
                stopwatch.ElapsedMilliseconds,
                ProcessExecutionErrorCodes.From(
                    ProcessExecutionError.UnsupportedExecutionMode));

            throw new McpException(
                $"{ProcessExecutionErrorCodes.From(ProcessExecutionError.UnsupportedExecutionMode)}: Execution mode '{executionMode}' is not supported.");
        }

        try
        {
            var result =
                await executionService.ExecuteAsync(
                    new ProcessExecutionRequest(
                        workspaceId,
                        executable,
                        arguments,
                        relativeWorkingDirectory,
                        timeoutSeconds,
                        maxOutputBytes,
                        parsedExecutionMode),
                    cancellationToken);

            stopwatch.Stop();

            var resultErrorCode =
                result.TimedOut
                    ? ProcessExecutionErrorCodes.ExecutionTimeout
                    : result.Cancelled
                        ? ProcessExecutionErrorCodes.ExecutionCancelled
                        : null;

            _ = auditWriter.TryWrite(
                new ProcessExecutionAuditRecord(
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Operation: "process_exec",
                    WorkspaceId: result.WorkspaceId,
                    RelativeWorkingDirectory:
                        result.RelativeWorkingDirectory,
                    Executable: result.Executable,
                    ArgumentCount: arguments.Length,
                    ExecutionMode:
                        parsedExecutionMode.ToString(),
                    DurationMilliseconds:
                        result.DurationMilliseconds,
                    ExitCode: result.ExitCode,
                    TimedOut: result.TimedOut,
                    Cancelled: result.Cancelled,
                    StandardOutputBytes:
                        Encoding.UTF8.GetByteCount(
                            result.StandardOutput),
                    StandardErrorBytes:
                        Encoding.UTF8.GetByteCount(
                            result.StandardError),
                    StandardOutputTruncated:
                        result.StandardOutputTruncated,
                    StandardErrorTruncated:
                        result.StandardErrorTruncated,
                    Success:
                        !result.TimedOut &&
                        !result.Cancelled,
                    ErrorCode: resultErrorCode));

            return result;
        }
        catch (ProcessExecutionException exception)
        {
            stopwatch.Stop();

            var code =
                ProcessExecutionErrorCodes.From(
                    exception.Error);

            WriteFailureAudit(
                auditWriter,
                workspaceId,
                executable,
                arguments.Length,
                parsedExecutionMode.ToString(),
                stopwatch.ElapsedMilliseconds,
                code);

            throw new McpException(
                $"{code}: {exception.Message}");
        }
    }

    private static void WriteFailureAudit(
        IAuditWriter auditWriter,
        string? workspaceId,
        string? executable,
        int argumentCount,
        string? executionMode,
        long durationMilliseconds,
        string? errorCode)
    {
        _ = auditWriter.TryWrite(
            new ProcessExecutionAuditRecord(
                TimestampUtc: DateTimeOffset.UtcNow,
                Operation: "process_exec",
                WorkspaceId: workspaceId,
                RelativeWorkingDirectory: null,
                Executable: executable,
                ArgumentCount: argumentCount,
                ExecutionMode: executionMode,
                DurationMilliseconds:
                    durationMilliseconds,
                ExitCode: null,
                TimedOut: false,
                Cancelled: false,
                StandardOutputBytes: 0,
                StandardErrorBytes: 0,
                StandardOutputTruncated: false,
                StandardErrorTruncated: false,
                Success: false,
                ErrorCode: errorCode));
    }
}
