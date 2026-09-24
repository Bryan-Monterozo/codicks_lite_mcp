namespace LocalAgent.Core.Files;

public sealed record PersistentMutationReceipt(
    string MutationId,
    string Operation,
    string Fingerprint,
    string WorkspaceId,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? RecoveryId,
    string? Sha256,
    PersistentMutationStatus Status,
    DateTimeOffset UpdatedAtUtc);
