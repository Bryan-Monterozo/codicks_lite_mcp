using System.Text.Json.Serialization;

namespace LocalAgent.Core.Files;

[JsonConverter(typeof(JsonStringEnumConverter<FileDiffLineKind>))]
public enum FileDiffLineKind
{
    [JsonStringEnumMemberName("context")]
    Context,

    [JsonStringEnumMemberName("add")]
    Addition,

    [JsonStringEnumMemberName("delete")]
    Deletion
}

public sealed record FileDiffLine(
    FileDiffLineKind Kind,
    string Text,
    int? OldLineNumber,
    int? NewLineNumber);

public sealed record FileDiffHunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<FileDiffLine> Lines);

public sealed record FileDiffRequest(
    string RelativePath,
    string CurrentContent,
    string ProposedContent,
    int ContextLines = 3,
    int MaxUnifiedDiffChars = 65536,
    int MaxStructuredLines = 2000);

public sealed record FileDiffResult(
    string RelativePath,
    string BaseSha256,
    string ProposedSha256,
    bool HasChanges,
    int Additions,
    int Deletions,
    int UnchangedLines,
    string UnifiedDiff,
    IReadOnlyList<FileDiffHunk> Hunks,
    string SourceEncoding,
    string SourceLineEnding,
    string ProposedLineEnding,
    bool Truncated,
    IReadOnlyList<string> Warnings,
    string ReviewState = "preview",
    string ReviewSummary = "");
