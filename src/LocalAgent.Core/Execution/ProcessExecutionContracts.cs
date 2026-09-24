namespace LocalAgent.Core.Execution;

public enum ExecutionMode
{
    Host,
    Sandbox
}

public enum ProcessExecutionError
{
    None,
    InvalidRequest,
    ExecutionDisabled,
    WorkspaceExecutionDenied,
    ExecutableNotAllowed,
    CommandNotAllowed,
    InvalidWorkingDirectory,
    TimeoutOutOfRange,
    OutputLimitOutOfRange,
    UnsupportedExecutionMode,
    WorkspaceNotFound,
    ProcessStartFailed,
    SandboxDisabled,
    SandboxUnavailable
}

public sealed record ProcessExecutionRequest(
    string WorkspaceId,
    string Executable,
    IReadOnlyList<string> Arguments,
    string RelativeWorkingDirectory = "",
    int? TimeoutSeconds = null,
    int? MaxOutputBytes = null,
    ExecutionMode ExecutionMode = LocalAgent.Core.Execution.ExecutionMode.Host);

public sealed record ProcessExecutionResult(
    string WorkspaceId,
    string RelativeWorkingDirectory,
    string Executable,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Cancelled,
    long DurationMilliseconds,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated);

public sealed record ExecutionPolicyDecision(
    bool Allowed,
    ProcessExecutionError Error,
    string Message,
    int EffectiveTimeoutSeconds,
    int EffectiveMaxOutputBytes)
{
    public static ExecutionPolicyDecision Allow(
        int effectiveTimeoutSeconds,
        int effectiveMaxOutputBytes) =>
        new(
            true,
            ProcessExecutionError.None,
            string.Empty,
            effectiveTimeoutSeconds,
            effectiveMaxOutputBytes);

    public static ExecutionPolicyDecision Deny(
        ProcessExecutionError error,
        string message) =>
        new(
            false,
            error,
            message,
            0,
            0);
}

public interface IExecutablePolicy
{
    ExecutionPolicyDecision Evaluate(
        LocalAgent.Core.Workspaces.WorkspaceDescriptor workspace,
        ProcessExecutionRequest request);
}

public sealed class ProcessExecutionException(
    ProcessExecutionError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ProcessExecutionError Error { get; } = error;
}

public interface IProcessExecutionService
{
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken);
}
