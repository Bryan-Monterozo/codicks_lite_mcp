namespace LocalAgent.Core.Security;

public sealed record FileSystemEntryInspection(
    FileSystemEntryKind Kind,
    long HardLinkCount,
    string? Detail = null);
