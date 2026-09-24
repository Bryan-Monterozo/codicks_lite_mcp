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

public interface IAuditWriter
{
    bool TryWrite(OperationAuditRecord record);
}
