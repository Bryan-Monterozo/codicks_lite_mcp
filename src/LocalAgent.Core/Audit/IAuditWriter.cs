namespace LocalAgent.Core.Audit;

public sealed record OperationAuditRecord(
    DateTimeOffset TimestampUtc,
    string Operation,
    string? WorkspaceId,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? MutationId,
    string? RecoveryId,
    bool DryRun,
    bool Success,
    string? ErrorCode);

public sealed record ProcessExecutionAuditRecord(
    DateTimeOffset TimestampUtc,
    string Operation,
    string? WorkspaceId,
    string? RelativeWorkingDirectory,
    string? Executable,
    int ArgumentCount,
    string? ExecutionMode,
    long DurationMilliseconds,
    int? ExitCode,
    bool TimedOut,
    bool Cancelled,
    int StandardOutputBytes,
    int StandardErrorBytes,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    bool Success,
    string? ErrorCode);

public interface IAuditWriter
{
    bool TryWrite(OperationAuditRecord record);

    bool TryWrite(ProcessExecutionAuditRecord record);
}
