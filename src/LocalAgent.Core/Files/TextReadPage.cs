namespace LocalAgent.Core.Files;

public sealed record TextReadPage(
    string RelativePath,
    string Content,
    long FileSizeBytes,
    string Encoding,
    string Sha256,
    long ByteOffset,
    long? NextByteOffset,
    bool IsPartial);
