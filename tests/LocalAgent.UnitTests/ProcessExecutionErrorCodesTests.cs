using LocalAgent.Core.Execution;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class ProcessExecutionErrorCodesTests
{
    [Theory]
    [InlineData(ProcessExecutionError.InvalidRequest, "INVALID_REQUEST")]
    [InlineData(ProcessExecutionError.ExecutionDisabled, "EXECUTION_DISABLED")]
    [InlineData(ProcessExecutionError.WorkspaceExecutionDenied, "WORKSPACE_EXECUTION_DENIED")]
    [InlineData(ProcessExecutionError.ExecutableNotAllowed, "EXECUTABLE_NOT_ALLOWED")]
    [InlineData(ProcessExecutionError.CommandNotAllowed, "COMMAND_NOT_ALLOWED")]
    [InlineData(ProcessExecutionError.InvalidWorkingDirectory, "INVALID_WORKING_DIRECTORY")]
    [InlineData(ProcessExecutionError.TimeoutOutOfRange, "TIMEOUT_OUT_OF_RANGE")]
    [InlineData(ProcessExecutionError.OutputLimitOutOfRange, "OUTPUT_LIMIT_OUT_OF_RANGE")]
    [InlineData(ProcessExecutionError.UnsupportedExecutionMode, "UNSUPPORTED_EXECUTION_MODE")]
    [InlineData(ProcessExecutionError.WorkspaceNotFound, "WORKSPACE_NOT_FOUND")]
    [InlineData(ProcessExecutionError.ProcessStartFailed, "PROCESS_START_FAILED")]
    public void From_ReturnsStableCode(
        ProcessExecutionError error,
        string expected)
    {
        Assert.Equal(
            expected,
            ProcessExecutionErrorCodes.From(error));
    }

    [Fact]
    public void ResultStateCodes_AreStable()
    {
        Assert.Equal(
            "EXECUTION_TIMEOUT",
            ProcessExecutionErrorCodes.ExecutionTimeout);

        Assert.Equal(
            "EXECUTION_CANCELLED",
            ProcessExecutionErrorCodes.ExecutionCancelled);

        Assert.Equal(
            "PROCESS_EXECUTION_ERROR",
            ProcessExecutionErrorCodes.GenericExecutionError);
    }
}
