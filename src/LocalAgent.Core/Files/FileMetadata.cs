using LocalAgent.Core.Security;

namespace LocalAgent.Core.Files;

public sealed record FileMetadata(
    string RelativePath,
    string Name,
    FileSystemEntryKind Kind,
    long? SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
