namespace LocalAgent.Infrastructure.Scratch;

public sealed record ScratchProbeResult(
    bool Success,
    string FileName,
    string FullPath,
    long BytesWritten,
    DateTimeOffset WrittenAtUtc);
