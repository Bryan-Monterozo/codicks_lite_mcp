namespace LocalAgent.Core.Execution;

public static class ProcessExecutionErrorCodes
{
    public const string ExecutionTimeout = "EXECUTION_TIMEOUT";
    public const string ExecutionCancelled = "EXECUTION_CANCELLED";
    public const string GenericExecutionError = "PROCESS_EXECUTION_ERROR";

    public static string From(ProcessExecutionError error) =>
        error switch
        {
            ProcessExecutionError.InvalidRequest => "INVALID_REQUEST",
            ProcessExecutionError.ExecutionDisabled => "EXECUTION_DISABLED",
            ProcessExecutionError.WorkspaceExecutionDenied => "WORKSPACE_EXECUTION_DENIED",
            ProcessExecutionError.ExecutableNotAllowed => "EXECUTABLE_NOT_ALLOWED",
            ProcessExecutionError.CommandNotAllowed => "COMMAND_NOT_ALLOWED",
            ProcessExecutionError.InvalidWorkingDirectory => "INVALID_WORKING_DIRECTORY",
            ProcessExecutionError.TimeoutOutOfRange => "TIMEOUT_OUT_OF_RANGE",
            ProcessExecutionError.OutputLimitOutOfRange => "OUTPUT_LIMIT_OUT_OF_RANGE",
            ProcessExecutionError.UnsupportedExecutionMode => "UNSUPPORTED_EXECUTION_MODE",
            ProcessExecutionError.WorkspaceNotFound => "WORKSPACE_NOT_FOUND",
            ProcessExecutionError.ProcessStartFailed => "PROCESS_START_FAILED",
            ProcessExecutionError.SandboxDisabled => "SANDBOX_DISABLED",
            ProcessExecutionError.SandboxUnavailable => "SANDBOX_UNAVAILABLE",
            _ => GenericExecutionError
        };
}
