using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Files;

public sealed class WorkspaceDiffService(
    IWorkspacePathPolicy pathPolicy,
    AgentConfiguration configuration,
    IFileHasher fileHasher,
    IFileDiffService diffService) : IWorkspaceDiffService
{
    private readonly long _maxEditableFileBytes =
        configuration.Agent.Limits.MaxEditableFileBytes;

    public QueryResult<FileDiffResult> DiffText(
        string workspaceId,
        string relativePath,
        string content,
        string? expectedHash = null)
    {
        if (content is null)
        {
            return QueryResult.Fail<FileDiffResult>(
                FileQueryError.InvalidRequest,
                "content is required.");
        }

        if (expectedHash is not null &&
            !MutationValidation.IsSha256(expectedHash))
        {
            return QueryResult.Fail<FileDiffResult>(
                FileQueryError.InvalidRequest,
                "expectedHash must be a 64-character hexadecimal SHA-256 value.");
        }

        var access = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: false);

        if (!access.Allowed ||
            access.FullPath is null ||
            access.NormalizedRelativePath is null)
        {
            return FromAccessFailure<FileDiffResult>(
                access);
        }

        if (access.EntryKind != FileSystemEntryKind.RegularFile)
        {
            return QueryResult.Fail<FileDiffResult>(
                FileQueryError.NotAFile,
                "Only regular text files can be diffed.");
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
                return QueryResult.Fail<FileDiffResult>(
                    FileQueryError.FileTooLarge,
                    $"Existing file exceeds the configured MaxEditableFileBytes limit of {_maxEditableFileBytes} bytes.");
            }

            currentBytes =
                File.ReadAllBytes(
                    access.FullPath);
        }
        catch (IOException exception)
        {
            return IoFailure<FileDiffResult>(
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<FileDiffResult>(
                exception);
        }

        var currentHash =
            fileHasher.Compute(
                currentBytes);

        if (expectedHash is not null &&
            !string.Equals(
                currentHash,
                expectedHash.Trim().ToUpperInvariant(),
                StringComparison.Ordinal))
        {
            return QueryResult.Fail<FileDiffResult>(
                FileQueryError.Conflict,
                "The file changed since it was read. Re-read it and retry with the current SHA-256 hash.");
        }

        var decodedCurrent =
            TextFileCodec.DecodeExisting(
                currentBytes);

        if (!decodedCurrent.Success)
        {
            return QueryResult.Fail<FileDiffResult>(
                FileQueryError.UnsupportedTextEncoding,
                decodedCurrent.Message);
        }

        var prepared =
            TextFileCodec.PrepareUpdate(
                currentBytes,
                content,
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
            return QueryResult.Fail<FileDiffResult>(
                FileQueryError.UnsupportedTextEncoding,
                decodedProposed.Message);
        }

        var result =
            diffService.CreateDiff(
                new FileDiffRequest(
                    access.NormalizedRelativePath,
                    decodedCurrent.Content,
                    decodedProposed.Content));

        var warnings =
            result.Warnings.ToList();

        if (!string.Equals(
                content,
                decodedProposed.Content,
                StringComparison.Ordinal))
        {
            warnings.Add(
                "Proposed content was normalized to the existing file line-ending style to match file_update behavior.");
        }

        var reviewResult =
            result with
            {
                BaseSha256 = currentHash,
                ProposedSha256 =
                    fileHasher.Compute(
                        prepared.Bytes),
                SourceEncoding =
                    prepared.Encoding,
                Warnings =
                    warnings.ToArray(),
                ReviewState =
                    FileReviewSummaryFormatter.PreviewState
            };

        return QueryResult.Ok(
            reviewResult with
            {
                ReviewSummary =
                    FileReviewSummaryFormatter.CreateDiffSummary(
                        reviewResult)
            });
    }

    private static QueryResult<T> FromAccessFailure<T>(
        PathPolicyResult result)
        where T : class
    {
        var error = result.Error switch
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

    private static QueryResult<FileDiffResult> PreparedTextFailure(
        string message)
    {
        var error =
            message.Contains(
                "MaxEditableFileBytes",
                StringComparison.Ordinal)
                ? FileQueryError.FileTooLarge
                : FileQueryError.UnsupportedTextEncoding;

        return QueryResult.Fail<FileDiffResult>(
            error,
            message);
    }

    private static QueryResult<T> IoFailure<T>(
        Exception exception)
        where T : class =>
        QueryResult.Fail<T>(
            FileQueryError.IoError,
            $"Filesystem diff preparation failed: {exception.Message}");
}
