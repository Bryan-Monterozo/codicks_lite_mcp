namespace LocalAgent.Core.Files;

public sealed record DirectoryPage(
    string RelativePath,
    IReadOnlyList<FileMetadata> Entries,
    int Offset,
    int? NextOffset,
    bool IsPartial);
