using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Files;

public sealed class WorkspacePatchApplyService(
    IWorkspacePathPolicy pathPolicy,
    AgentConfiguration configuration,
    IFileHasher fileHasher,
    IUnifiedPatchService patchService,
    IFileDiffService diffService,
    IFilePatchReviewStore reviewStore,
    IWorkspaceMutationService mutationService) : IWorkspacePatchApplyService
{
    private readonly long _maxEditableFileBytes =
        configuration.Agent.Limits.MaxEditableFileBytes;

    private readonly int _maxPatchBytes =
        CalculateMaxPatchBytes(
            configuration.Agent.Limits.MaxEditableFileBytes);

    public MutationResult<FileMutationReceipt> Apply(
        string workspaceId,
        string relativePath,
        string patch,
        string expectedHash,
        string reviewToken)
    {
        if (patch is null)
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.InvalidRequest,
                "patch is required.");
        }

        if (!MutationValidation.IsSha256(
                expectedHash))
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.InvalidRequest,
                "expectedHash must be a 64-character hexadecimal SHA-256 value.");
        }

        var access =
            pathPolicy.ValidateExisting(
                workspaceId,
                relativePath,
                WorkspaceOperation.Update,
                allowWorkspaceRoot: false);

        if (!access.Allowed ||
            access.FullPath is null ||
            access.NormalizedRelativePath is null)
        {
            return FromAccessFailure<FileMutationReceipt>(
                access);
        }

        if (access.EntryKind !=
            FileSystemEntryKind.RegularFile)
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.NotAFile,
                "Only regular text files can be patched.");
        }

        var validation =
            reviewStore.Validate(
                reviewToken);

        if (!validation.Valid ||
            validation.Receipt is null)
        {
            return ReviewValidationFailure(
                validation.Error);
        }

        var receipt =
            validation.Receipt;

        var normalizedExpectedHash =
            expectedHash.Trim().ToUpperInvariant();

        if (!string.Equals(
                receipt.Binding.WorkspaceId,
                workspaceId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                receipt.Binding.RelativePath,
                access.NormalizedRelativePath,
                StringComparison.Ordinal) ||
            !string.Equals(
                receipt.Binding.BaseSha256,
                normalizedExpectedHash,
                StringComparison.Ordinal))
        {
            return InvalidReviewToken(
                "Review token does not match the requested workspace, path, or base hash.");
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
                return MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.FileTooLarge,
                    $"Existing file exceeds the configured MaxEditableFileBytes limit of {_maxEditableFileBytes} bytes.");
            }

            currentBytes =
                File.ReadAllBytes(
                    access.FullPath);
        }
        catch (IOException exception)
        {
            return IoFailure<FileMutationReceipt>(
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<FileMutationReceipt>(
                exception);
        }

        var currentHash =
            fileHasher.Compute(
                currentBytes);

        if (!string.Equals(
                currentHash,
                normalizedExpectedHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                currentHash,
                receipt.Binding.BaseSha256,
                StringComparison.Ordinal))
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.Conflict,
                "The file changed after preview. Re-read and preview the patch again.");
        }

        var decodedCurrent =
            TextFileCodec.DecodeExisting(
                currentBytes);

        if (!decodedCurrent.Success)
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.UnsupportedTextEncoding,
                decodedCurrent.Message);
        }

        var application =
            patchService.Apply(
                new FilePatchRequest(
                    access.NormalizedRelativePath,
                    decodedCurrent.Content,
                    patch,
                    _maxPatchBytes));

        if (!string.Equals(
                application.PatchSha256,
                receipt.Binding.PatchSha256,
                StringComparison.Ordinal))
        {
            return InvalidReviewToken(
                "Patch content does not match the reviewed patch.");
        }

        if (!application.Success)
        {
            return PatchFailure(
                application);
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

        var proposedHash =
            fileHasher.Compute(
                prepared.Bytes);

        if (!string.Equals(
                proposedHash,
                receipt.Binding.ProposedSha256,
                StringComparison.Ordinal))
        {
            return InvalidReviewToken(
                "Recomputed proposed content does not match the reviewed result.");
        }

        var decodedProposed =
            TextFileCodec.DecodeExisting(
                prepared.Bytes);

        if (!decodedProposed.Success)
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.UnsupportedTextEncoding,
                decodedProposed.Message);
        }

        var diff =
            diffService.CreateDiff(
                new FileDiffRequest(
                    access.NormalizedRelativePath,
                    decodedCurrent.Content,
                    decodedProposed.Content));

        if (!diff.HasChanges ||
            diff.Truncated)
        {
            return InvalidReviewToken(
                "Recomputed canonical review is not applicable.");
        }

        var reviewHash =
            FilePatchReviewHash.Compute(
                access.NormalizedRelativePath,
                currentHash,
                proposedHash,
                application.PatchSha256,
                diff.UnifiedDiff);

        if (!string.Equals(
                reviewHash,
                receipt.Binding.ReviewSha256,
                StringComparison.Ordinal))
        {
            return InvalidReviewToken(
                "Canonical review does not match the reviewed preview.");
        }

        var update =
            mutationService.UpdateText(
                workspaceId,
                access.NormalizedRelativePath,
                application.ProposedContent,
                normalizedExpectedHash,
                dryRun: false);

        if (!update.Success)
        {
            return update;
        }

        if (!reviewStore.TryConsume(
                reviewToken))
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.MutationStateConflict,
                "Patch was applied, but the review token could not be marked consumed.");
        }

        return update;
    }

    private static MutationResult<FileMutationReceipt> ReviewValidationFailure(
        FilePatchReviewValidationError error) =>
        error switch
        {
            FilePatchReviewValidationError.ReviewRequired =>
                MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.ReviewRequired,
                    "A review token from file_patch_preview is required."),

            FilePatchReviewValidationError.ExpiredToken =>
                MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.ExpiredReviewToken,
                    "The review token has expired. Preview the patch again."),

            FilePatchReviewValidationError.ConsumedToken =>
                MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.ConsumedReviewToken,
                    "The review token has already been consumed."),

            _ =>
                InvalidReviewToken(
                    "The review token is unknown or invalid.")
        };

    private static MutationResult<FileMutationReceipt> InvalidReviewToken(
        string message) =>
        MutationResult.Fail<FileMutationReceipt>(
            FileMutationError.InvalidReviewToken,
            message);

    private static MutationResult<FileMutationReceipt> PatchFailure(
        FilePatchApplicationResult application) =>
        application.Error switch
        {
            FilePatchError.TooLarge =>
                MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.PatchTooLarge,
                    application.Message),

            FilePatchError.Conflict =>
                MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.PatchConflict,
                    application.Message),

            _ =>
                MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.InvalidPatch,
                    application.Message)
        };

    private static MutationResult<FileMutationReceipt> PreparedTextFailure(
        string message)
    {
        var error =
            message.Contains(
                "MaxEditableFileBytes",
                StringComparison.Ordinal)
                ? FileMutationError.FileTooLarge
                : FileMutationError.UnsupportedTextEncoding;

        return MutationResult.Fail<FileMutationReceipt>(
            error,
            message);
    }

    private static MutationResult<T> FromAccessFailure<T>(
        PathPolicyResult result)
        where T : class
    {
        var error =
            result.Error switch
            {
                WorkspaceAccessError.PathNotFound =>
                    FileMutationError.NotFound,

                WorkspaceAccessError.ParentNotFound =>
                    FileMutationError.NotFound,

                WorkspaceAccessError.InvalidRelativePath =>
                    FileMutationError.InvalidRequest,

                WorkspaceAccessError.WorkspaceRootNotAllowed =>
                    FileMutationError.InvalidRequest,

                WorkspaceAccessError.InspectionFailed =>
                    FileMutationError.IoError,

                _ =>
                    FileMutationError.AccessDenied
            };

        return MutationResult.Fail<T>(
            error,
            result.Message,
            result.Error);
    }

    private static MutationResult<T> IoFailure<T>(
        Exception exception)
        where T : class =>
        MutationResult.Fail<T>(
            FileMutationError.IoError,
            $"Filesystem patch apply failed: {exception.Message}");

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
}
