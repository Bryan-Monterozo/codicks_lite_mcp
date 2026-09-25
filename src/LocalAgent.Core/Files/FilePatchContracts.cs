namespace LocalAgent.Core.Files;

public enum FilePatchError
{
    None,
    InvalidPatch,
    Conflict,
    TooLarge
}

public sealed record FilePatchRequest(
    string RelativePath,
    string CurrentContent,
    string Patch,
    int MaxPatchBytes);

public sealed record FilePatchApplicationResult(
    bool Success,
    string ProposedContent,
    string PatchSha256,
    FilePatchError Error,
    string Message)
{
    public static FilePatchApplicationResult Ok(
        string proposedContent,
        string patchSha256) =>
        new(
            true,
            proposedContent,
            patchSha256,
            FilePatchError.None,
            string.Empty);

    public static FilePatchApplicationResult Fail(
        FilePatchError error,
        string message,
        string patchSha256 = "") =>
        new(
            false,
            string.Empty,
            patchSha256,
            error,
            message);
}

public sealed record FilePatchPreviewResult(
    string RelativePath,
    string BaseSha256,
    string ProposedSha256,
    string PatchSha256,
    bool CanApply,
    int Additions,
    int Deletions,
    string UnifiedDiff,
    IReadOnlyList<FileDiffHunk> Hunks,
    string SourceEncoding,
    string SourceLineEnding,
    string ProposedLineEnding,
    bool Truncated,
    bool DiffIncluded,
    IReadOnlyList<string> Warnings,
    string? ReviewToken,
    DateTimeOffset? ReviewExpiresAtUtc,
    string ReviewState = "preview",
    string ReviewSummary = "");

public sealed record FilePatchApplyResult(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string Encoding,
    string LineEnding,
    string? BackupId,
    string ApplyState,
    string ApplySummary);

public interface IUnifiedPatchService
{
    FilePatchApplicationResult Apply(
        FilePatchRequest request);
}

public interface IWorkspacePatchPreviewService
{
    QueryResult<FilePatchPreviewResult> Preview(
        string workspaceId,
        string relativePath,
        string patch,
        string expectedHash,
        bool includeDiff = false);
}


public sealed record FilePatchReviewBinding(
    string WorkspaceId,
    string RelativePath,
    string BaseSha256,
    string ProposedSha256,
    string PatchSha256,
    string ReviewSha256);

public sealed record FilePatchReviewReceipt(
    string Token,
    FilePatchReviewBinding Binding,
    string Patch,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool Consumed);

public enum FilePatchReviewValidationError
{
    None,
    ReviewRequired,
    InvalidToken,
    ExpiredToken,
    ConsumedToken
}

public sealed record FilePatchReviewValidation(
    bool Valid,
    FilePatchReviewReceipt? Receipt,
    FilePatchReviewValidationError Error);

public interface IFilePatchReviewStore
{
    FilePatchReviewReceipt Issue(
        FilePatchReviewBinding binding,
        string patch);

    FilePatchReviewValidation Validate(
        string? token);

    bool TryConsume(
        string token);
}

public interface IWorkspacePatchApplyService
{
    MutationResult<FileMutationReceipt> Apply(
        string workspaceId,
        string relativePath,
        string? patch,
        string expectedHash,
        string reviewToken);
}
