namespace LocalAgent.Core.Files;

public sealed record FileMutationReceipt(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string Encoding,
    string LineEnding,
    bool DryRun,
    string? BackupId);
