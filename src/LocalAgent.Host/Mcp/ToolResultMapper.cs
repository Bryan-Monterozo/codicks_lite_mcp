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
internal sealed record MutationAuditContext(
    string Operation,
    string? WorkspaceId,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? MutationId,
    string? RecoveryId,
    bool DryRun);
