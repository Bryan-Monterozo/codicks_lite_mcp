namespace LocalAgent.Core.Recovery;

public sealed record RecoveryRecord(
    string Id,
    string WorkspaceId,
    string OriginalRelativePath,
    string QuarantinePath,
    string Sha256,
    long SizeBytes,
    RecoveryStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
