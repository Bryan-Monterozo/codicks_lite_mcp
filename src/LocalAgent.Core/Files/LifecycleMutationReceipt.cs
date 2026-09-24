namespace LocalAgent.Core.Files;

public sealed record LifecycleMutationReceipt(
    string MutationId,
    string Operation,
    string WorkspaceId,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? RecoveryId,
    string? Sha256,
    bool Replayed,
    bool Reconciled);
