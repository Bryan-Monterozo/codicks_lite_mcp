using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Files;

public sealed class WorkspacePatchPreviewService(
    IWorkspacePathPolicy pathPolicy,
    AgentConfiguration configuration,
    IFileHasher fileHasher,
    IUnifiedPatchService patchService,
    IFileDiffService diffService,
    IFilePatchReviewStore reviewStore) : IWorkspacePatchPreviewService
{
    private readonly long _maxEditableFileBytes =
        configuration.Agent.Limits.MaxEditableFileBytes;

    private readonly int _maxPatchBytes =
        CalculateMaxPatchBytes(
            configuration.Agent.Limits.MaxEditableFileBytes);

    public QueryResult<FilePatchPreviewResult> Preview(
        string workspaceId,
        string relativePath,
        string patch,
        string expectedHash)
    {
        if (patch is null)
        {
            return QueryResult.Fail<FilePatchPreviewResult>(
                FileQueryError.InvalidRequest,
                "patch is required.");
        }

        if (!MutationValidation.IsSha256(
                expectedHash))
        {
            return QueryResult.Fail<FilePatchPreviewResult>(
                FileQueryError.InvalidRequest,
                "expectedHash must be a 64-character hexadecimal SHA-256 value.");
        }

        var access =
            pathPolicy.ValidateExisting(
                workspaceId,
                relativePath,
                WorkspaceOperation.Read,
                allowWorkspaceRoot: false);

        if (!access.Allowed ||
            access.FullPath is null ||
            access.NormalizedRelativePath is null)
        {
            return FromAccessFailure<FilePatchPreviewResult>(
                access);
        }

        if (access.EntryKind !=
            FileSystemEntryKind.RegularFile)
        {
            return QueryResult.Fail<FilePatchPreviewResult>(
                FileQueryError.NotAFile,
                "Only regular text files can be patched.");
        }

        byte[] currentBytes;

        try
        {
            var fileInfo =
                new FileInfo(
                    access.FullPath);

            if (fileInfo.Length >
                _maxEditableFileBytes)
            {
                return QueryResult.Fail<FilePatchPreviewResult>(
                    FileQueryError.FileTooLarge,
                    $"Existing file exceeds the configured MaxEditableFileBytes limit of {_maxEditableFileBytes} bytes.");
            }

            currentBytes =
                File.ReadAllBytes(
                    access.FullPath);
        }
        catch (IOException exception)
        {
            return IoFailure<FilePatchPreviewResult>(
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<FilePatchPreviewResult>(
                exception);
        }

        var currentHash =
            fileHasher.Compute(
                currentBytes);

        if (!string.Equals(
                currentHash,
                expectedHash.Trim().ToUpperInvariant(),
                StringComparison.Ordinal))
        {
            return QueryResult.Fail<FilePatchPreviewResult>(
                FileQueryError.Conflict,
                "The file changed since it was read. Re-read it and retry with the current SHA-256 hash.");
        }

        var decodedCurrent =
            TextFileCodec.DecodeExisting(
                currentBytes);

        if (!decodedCurrent.Success)
        {
            return QueryResult.Fail<FilePatchPreviewResult>(
                FileQueryError.UnsupportedTextEncoding,
                decodedCurrent.Message);
        }

        var application =
            patchService.Apply(
                new FilePatchRequest(
                    access.NormalizedRelativePath,
                    decodedCurrent.Content,
                    patch,
                    _maxPatchBytes));

        if (!application.Success)
        {
            return application.Error switch
            {
                FilePatchError.TooLarge =>
                    QueryResult.Fail<FilePatchPreviewResult>(
                        FileQueryError.PatchTooLarge,
                        application.Message),

                FilePatchError.Conflict =>
                    QueryResult.Fail<FilePatchPreviewResult>(
                        FileQueryError.PatchConflict,
                        application.Message),

                _ =>
                    QueryResult.Fail<FilePatchPreviewResult>(
                        FileQueryError.InvalidPatch,
                        application.Message)
            };
        }

        var prepared =
            TextFileCodec.PrepareUpdate(
                currentBytes,
                application.ProposedContent,
                _maxEditableFileBytes);

        if (!prepared.Success)
        {
            return PreparedTextFailure(
                prepared.Message);
        }

        var decodedProposed =
            TextFileCodec.DecodeExisting(
                prepared.Bytes);

        if (!decodedProposed.Success)
        {
            return QueryResult.Fail<FilePatchPreviewResult>(
                FileQueryError.UnsupportedTextEncoding,
                decodedProposed.Message);
        }

        var diff =
            diffService.CreateDiff(
                new FileDiffRequest(
                    access.NormalizedRelativePath,
                    decodedCurrent.Content,
                    decodedProposed.Content));

        var warnings =
            diff.Warnings.ToList();

        if (!string.Equals(
                application.ProposedContent,
                decodedProposed.Content,
                StringComparison.Ordinal))
        {
            warnings.Add(
                "Patch result was normalized to the existing file line-ending style to match file_update behavior.");
        }

        if (!diff.HasChanges)
        {
            warnings.Add(
                "Patch produces no content changes.");
        }

        if (diff.Truncated)
        {
            warnings.Add(
                "Canonical review output is truncated; canApply is false for this preview.");
        }

        var proposedHash =
            fileHasher.Compute(
                prepared.Bytes);

        var canApply =
            diff.HasChanges &&
            !diff.Truncated;

        FilePatchReviewReceipt? reviewReceipt =
            null;

        if (canApply)
        {
            var reviewHash =
                FilePatchReviewHash.Compute(
                    access.NormalizedRelativePath,
                    currentHash,
                    proposedHash,
                    application.PatchSha256,
                    diff.UnifiedDiff);

            reviewReceipt =
                reviewStore.Issue(
                    new FilePatchReviewBinding(
                        workspaceId,
                        access.NormalizedRelativePath,
                        currentHash,
                        proposedHash,
                        application.PatchSha256,
                        reviewHash));
        }

        var reviewResult =
            new FilePatchPreviewResult(
                access.NormalizedRelativePath,
                currentHash,
                proposedHash,
                application.PatchSha256,
                canApply,
                diff.Additions,
                diff.Deletions,
                diff.UnifiedDiff,
                diff.Hunks,
                prepared.Encoding,
                diff.SourceLineEnding,
                diff.ProposedLineEnding,
                diff.Truncated,
                warnings.ToArray(),
                reviewReceipt?.Token,
                reviewReceipt?.ExpiresAtUtc,
                FileReviewSummaryFormatter.PreviewState);

        return QueryResult.Ok(
            reviewResult with
            {
                ReviewSummary =
                    FileReviewSummaryFormatter.CreatePatchPreviewSummary(
                        reviewResult)
            });
    }

    private static int CalculateMaxPatchBytes(
        long maxEditableFileBytes)
    {
        const long overhead = 65_536;

        if (maxEditableFileBytes >=
            (int.MaxValue - overhead) / 2)
        {
            return int.MaxValue;
        }

        return checked(
            (int)(
                (maxEditableFileBytes * 2) +
                overhead));
    }

    private static QueryResult<T> FromAccessFailure<T>(
        PathPolicyResult result)
        where T : class
    {
        var error =
            result.Error switch
            {
                WorkspaceAccessError.PathNotFound =>
                    FileQueryError.NotFound,

                WorkspaceAccessError.ParentNotFound =>
                    FileQueryError.NotFound,

                WorkspaceAccessError.InvalidRelativePath =>
                    FileQueryError.InvalidRequest,

                WorkspaceAccessError.WorkspaceRootNotAllowed =>
                    FileQueryError.InvalidRequest,

                WorkspaceAccessError.InspectionFailed =>
                    FileQueryError.IoError,

                _ =>
                    FileQueryError.AccessDenied
            };

        return QueryResult.Fail<T>(
            error,
            result.Message,
            result.Error);
    }

    private static QueryResult<FilePatchPreviewResult> PreparedTextFailure(
        string message)
    {
        var error =
            message.Contains(
                "MaxEditableFileBytes",
                StringComparison.Ordinal)
                ? FileQueryError.FileTooLarge
                : FileQueryError.UnsupportedTextEncoding;

        return QueryResult.Fail<FilePatchPreviewResult>(
            error,
            message);
    }

    private static QueryResult<T> IoFailure<T>(
        Exception exception)
        where T : class =>
        QueryResult.Fail<T>(
            FileQueryError.IoError,
            $"Filesystem patch preview failed: {exception.Message}");
}
