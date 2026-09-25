using LocalAgent.Core.Audit;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using ModelContextProtocol;

namespace LocalAgent.Host.Mcp;

internal static class ToolResultMapper
{
    public static T RequireValue<T>(QueryResult<T> result)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Success && result.Value is not null)
        {
            return result.Value;
        }

        throw CreateException(
            MapQueryError(result.Error, result.AccessError),
            result.Message);
    }

    public static T RequireValue<T>(MutationResult<T> result)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Success && result.Value is not null)
        {
            return result.Value;
        }

        throw CreateException(
            MapMutationError(result.Error, result.AccessError),
            result.Message);
    }

    public static T RequireValue<T>(
        QueryResult<T> result,
        IAuditWriter auditWriter,
        FileReviewAuditContext auditContext)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(auditContext);

        var relativePath =
            auditContext.RelativePath;

        var baseSha256 =
            auditContext.BaseSha256;

        var proposedSha256 =
            auditContext.ProposedSha256;

        var patchSha256 =
            auditContext.PatchSha256;

        if (result.Value is FileDiffResult diff)
        {
            relativePath =
                diff.RelativePath;

            baseSha256 =
                diff.BaseSha256;

            proposedSha256 =
                diff.ProposedSha256;
        }
        else if (result.Value is FilePatchPreviewResult preview)
        {
            relativePath =
                preview.RelativePath;

            baseSha256 =
                preview.BaseSha256;

            proposedSha256 =
                preview.ProposedSha256;

            patchSha256 =
                preview.PatchSha256;
        }

        var errorCode =
            result.Success
                ? null
                : MapQueryError(
                    result.Error,
                    result.AccessError);

        WriteFileReviewAudit(
            auditWriter,
            auditContext,
            relativePath,
            baseSha256,
            proposedSha256,
            patchSha256,
            result.Success,
            errorCode,
            backupId: null);

        return RequireValue(result);
    }

    public static T RequireValue<T>(
        MutationResult<T> result,
        IAuditWriter auditWriter,
        FileReviewAuditContext auditContext)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(auditContext);

        var proposedSha256 =
            auditContext.ProposedSha256;

        var backupId =
            auditContext.BackupId;

        if (result.Value is FileMutationReceipt receipt)
        {
            proposedSha256 =
                receipt.Sha256;

            backupId =
                receipt.BackupId;
        }

        var errorCode =
            result.Success
                ? null
                : MapMutationError(
                    result.Error,
                    result.AccessError);

        WriteFileReviewAudit(
            auditWriter,
            auditContext,
            auditContext.RelativePath,
            auditContext.BaseSha256,
            proposedSha256,
            auditContext.PatchSha256,
            result.Success,
            errorCode,
            backupId);

        return RequireValue(result);
    }

    public static void RequireReviewAuthorization(
        SessionAuthorizationResult authorization,
        IAuditWriter auditWriter,
        FileReviewAuditContext auditContext)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(auditContext);

        if (authorization.Allowed)
        {
            return;
        }

        var errorCode =
            string.IsNullOrWhiteSpace(
                authorization.ErrorCode)
                ? "ACCESS_DENIED"
                : authorization.ErrorCode;

        var message =
            string.IsNullOrWhiteSpace(
                authorization.Message)
                ? "Session authorization denied."
                : authorization.Message;

        WriteFileReviewAudit(
            auditWriter,
            auditContext,
            auditContext.RelativePath,
            auditContext.BaseSha256,
            auditContext.ProposedSha256,
            auditContext.PatchSha256,
            success: false,
            errorCode,
            auditContext.BackupId);

        throw CreateException(
            errorCode,
            message);
    }

    private static void WriteFileReviewAudit(
        IAuditWriter auditWriter,
        FileReviewAuditContext auditContext,
        string? relativePath,
        string? baseSha256,
        string? proposedSha256,
        string? patchSha256,
        bool success,
        string? errorCode,
        string? backupId)
    {
        _ = auditWriter.TryWrite(
            new FileReviewAuditRecord(
                DateTimeOffset.UtcNow,
                auditContext.Operation,
                auditContext.WorkspaceId,
                relativePath,
                baseSha256,
                proposedSha256,
                patchSha256,
                auditContext.Preview,
                success,
                errorCode,
                backupId));
    }

    public static T RequireValue<T>(
        MutationResult<T> result,
        IAuditWriter auditWriter,
        MutationAuditContext auditContext)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(auditContext);

        var workspaceId = auditContext.WorkspaceId;
        var sourcePath = auditContext.SourceRelativePath;
        var destinationPath = auditContext.DestinationRelativePath;
        var recoveryId = auditContext.RecoveryId;

        if (result.Value is LifecycleMutationReceipt lifecycleReceipt)
        {
            workspaceId = lifecycleReceipt.WorkspaceId;
            sourcePath ??= lifecycleReceipt.SourceRelativePath;
            destinationPath ??= lifecycleReceipt.DestinationRelativePath;
            recoveryId ??= lifecycleReceipt.RecoveryId;
        }

        var errorCode = result.Success
            ? null
            : MapMutationError(result.Error, result.AccessError);

        _ = auditWriter.TryWrite(new OperationAuditRecord(
            DateTimeOffset.UtcNow,
            auditContext.Operation,
            workspaceId,
            sourcePath,
            destinationPath,
            auditContext.MutationId,
            recoveryId,
            auditContext.DryRun,
            result.Success,
            errorCode));

        return RequireValue(result);
    }

    private static McpException CreateException(
        string code,
        string message)
    {
        var detail = string.IsNullOrWhiteSpace(message)
            ? "Operation failed."
            : message.Trim();

        return new McpException($"{code}: {detail}");
    }

    private static string MapQueryError(
        FileQueryError error,
        WorkspaceAccessError? accessError)
    {
        var accessCode = MapAccessError(accessError);
        if (accessCode is not null)
        {
            return accessCode;
        }

        return error switch
        {
            FileQueryError.InvalidRequest => "INVALID_REQUEST",
            FileQueryError.AccessDenied => "ACCESS_DENIED",
            FileQueryError.NotFound => "FILE_NOT_FOUND",
            FileQueryError.NotAFile => "UNSUPPORTED_FILE_TYPE",
            FileQueryError.NotADirectory => "UNSUPPORTED_FILE_TYPE",
            FileQueryError.UnsupportedTextEncoding => "UNSUPPORTED_FILE_TYPE",
            FileQueryError.Conflict => "CONFLICT",
            FileQueryError.FileTooLarge => "FILE_TOO_LARGE",
            FileQueryError.InvalidPatch => "INVALID_PATCH",
            FileQueryError.PatchConflict => "PATCH_CONFLICT",
            FileQueryError.PatchTooLarge => "PATCH_TOO_LARGE",
            FileQueryError.IoError => "IO_ERROR",
            _ => "UNKNOWN_ERROR"
        };
    }

    private static string MapMutationError(
        FileMutationError error,
        WorkspaceAccessError? accessError)
    {
        var accessCode = MapAccessError(accessError);
        if (accessCode is not null)
        {
            return accessCode;
        }

        return error switch
        {
            FileMutationError.InvalidRequest => "INVALID_REQUEST",
            FileMutationError.AccessDenied => "ACCESS_DENIED",
            FileMutationError.AlreadyExists => "ALREADY_EXISTS",
            FileMutationError.NotFound => "FILE_NOT_FOUND",
            FileMutationError.NotAFile => "UNSUPPORTED_FILE_TYPE",
            FileMutationError.NotADirectory => "UNSUPPORTED_FILE_TYPE",
            FileMutationError.Conflict => "CONFLICT",
            FileMutationError.FileTooLarge => "FILE_TOO_LARGE",
            FileMutationError.UnsupportedTextEncoding => "UNSUPPORTED_FILE_TYPE",
            FileMutationError.BackupFailed => "RECOVERY_REQUIRED",
            FileMutationError.AtomicWriteFailed => "IO_ERROR",
            FileMutationError.IoError => "IO_ERROR",
            FileMutationError.RecoveryNotFound => "RECOVERY_REQUIRED",
            FileMutationError.RecoveryUnavailable => "RECOVERY_REQUIRED",
            FileMutationError.MutationIdConflict => "CONFLICT",
            FileMutationError.MutationStateConflict => "CONFLICT",
            FileMutationError.ReviewRequired => "REVIEW_REQUIRED",
            FileMutationError.InvalidReviewToken => "INVALID_REVIEW_TOKEN",
            FileMutationError.ExpiredReviewToken => "EXPIRED_REVIEW_TOKEN",
            FileMutationError.ConsumedReviewToken => "CONSUMED_REVIEW_TOKEN",
            FileMutationError.InvalidPatch => "INVALID_PATCH",
            FileMutationError.PatchConflict => "PATCH_CONFLICT",
            FileMutationError.PatchTooLarge => "PATCH_TOO_LARGE",
            _ => "UNKNOWN_ERROR"
        };
    }

    private static string? MapAccessError(
        WorkspaceAccessError? accessError)
    {
        if (accessError is null or WorkspaceAccessError.None)
        {
            return null;
        }

        return accessError.Value switch
        {
            WorkspaceAccessError.InvalidWorkspaceId => "WORKSPACE_NOT_FOUND",
            WorkspaceAccessError.WorkspaceNotFound => "WORKSPACE_NOT_FOUND",
            WorkspaceAccessError.WorkspaceDisabled => "ACCESS_DENIED",
            WorkspaceAccessError.GlobalReadOnly => "ACCESS_DENIED",
            WorkspaceAccessError.OperationDenied => "ACCESS_DENIED",
            WorkspaceAccessError.InvalidRelativePath => "INVALID_PATH",
            WorkspaceAccessError.PathOutsideWorkspace => "PATH_OUTSIDE_WORKSPACE",
            WorkspaceAccessError.WorkspaceRootNotAllowed => "INVALID_PATH",
            WorkspaceAccessError.PathDeniedByPolicy => "ACCESS_DENIED",
            WorkspaceAccessError.PathNotFound => "FILE_NOT_FOUND",
            WorkspaceAccessError.ParentNotFound => "FILE_NOT_FOUND",
            WorkspaceAccessError.PathAlreadyExists => "ALREADY_EXISTS",
            WorkspaceAccessError.SymbolicLinkNotAllowed => "ACCESS_DENIED",
            WorkspaceAccessError.HardLinkNotAllowed => "ACCESS_DENIED",
            WorkspaceAccessError.UnsupportedEntryType => "UNSUPPORTED_FILE_TYPE",
            WorkspaceAccessError.InspectionFailed => "IO_ERROR",
            _ => "UNKNOWN_ERROR"
        };
    }
}

internal sealed record FileReviewAuditContext(
    string Operation,
    string? WorkspaceId,
    string? RelativePath,
    string? BaseSha256,
    string? ProposedSha256,
    string? PatchSha256,
    bool Preview,
    string? BackupId);

internal sealed record MutationAuditContext(
    string Operation,
    string? WorkspaceId,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? MutationId,
    string? RecoveryId,
    bool DryRun);
