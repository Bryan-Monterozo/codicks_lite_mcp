using System.ComponentModel;
using LocalAgent.Core.Audit;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class FileMutationTools
{
    [McpServerTool(
        Name = "file_create",
        Title = "Create workspace text file",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Creates a new UTF-8 text file inside an approved workspace. Existing destinations are never overwritten. Set dryRun=true to validate and project the result without writing.")]
    public static FileMutationReceipt FileCreate(
        IWorkspaceMutationService mutationService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative destination file path.")] string relativePath,
        [Description("Complete text content for the new file.")] string content,
        [Description("Validate and project without changing the filesystem.")] bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(mutationService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        SessionAuthorization.RequireMutation(sessionGuard);

        var result = mutationService.CreateText(
                workspaceId,
                relativePath,
                content,
                dryRun);

        return ToolResultMapper.RequireValue(
            result,
            auditWriter,
            new MutationAuditContext(
                "file_create",
                workspaceId,
                relativePath,
                null,
                null,
                null,
                dryRun));
    }

    [McpServerTool(
        Name = "directory_create",
        Title = "Create workspace directory",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Creates a workspace-relative directory path using Chunk 03 component-by-component validation. mutationId makes retries replay-safe; use 8-128 letters, digits, '.', '_' or '-'.")]
    public static LifecycleMutationReceipt DirectoryCreate(
        IWorkspaceLifecycleService lifecycleService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Stable unique id for this mutation/retry sequence.")] string mutationId,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative directory path to create.")] string relativePath)
    {
        ArgumentNullException.ThrowIfNull(lifecycleService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        SessionAuthorization.RequireMutation(sessionGuard);

        var result = lifecycleService.CreateDirectory(
                mutationId,
                workspaceId,
                relativePath);

        return ToolResultMapper.RequireValue(
            result,
            auditWriter,
            new MutationAuditContext(
                "directory_create",
                workspaceId,
                relativePath,
                relativePath,
                mutationId,
                null,
                false));
    }

    [McpServerTool(
        Name = "file_update",
        Title = "Update workspace text file",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Replaces the complete contents of an approved text file using the SHA-256 from a prior file_read. Stale hashes return CONFLICT. A recovery backup is written before replacement. Set dryRun=true to validate without writing.")]
    public static FileMutationReceipt FileUpdate(
        IWorkspaceMutationService mutationService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative existing text file path.")] string relativePath,
        [Description("Complete replacement text content.")] string content,
        [Description("64-character SHA-256 returned by the prior file_read.")] string expectedHash,
        [Description("Validate and project without changing the filesystem or writing a backup.")] bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(mutationService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        SessionAuthorization.RequireMutation(sessionGuard);

        var result = mutationService.UpdateText(
                workspaceId,
                relativePath,
                content,
                expectedHash,
                dryRun);

        return ToolResultMapper.RequireValue(
            result,
            auditWriter,
            new MutationAuditContext(
                "file_update",
                workspaceId,
                relativePath,
                relativePath,
                null,
                null,
                dryRun));
    }

    [McpServerTool(
        Name = "file_move",
        Title = "Move workspace file",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Moves or renames one regular file inside the same approved workspace. Requires the source SHA-256 and refuses destination overwrite. mutationId makes retries replay-safe.")]
    public static LifecycleMutationReceipt FileMove(
        IWorkspaceLifecycleService lifecycleService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Stable unique id for this mutation/retry sequence.")] string mutationId,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative existing source file path.")] string sourceRelativePath,
        [Description("Workspace-relative destination file path.")] string destinationRelativePath,
        [Description("64-character SHA-256 of the current source file.")] string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(lifecycleService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        SessionAuthorization.RequireMutation(sessionGuard);

        var result = lifecycleService.MoveFile(
                mutationId,
                workspaceId,
                sourceRelativePath,
                destinationRelativePath,
                expectedHash);

        return ToolResultMapper.RequireValue(
            result,
            auditWriter,
            new MutationAuditContext(
                "file_move",
                workspaceId,
                sourceRelativePath,
                destinationRelativePath,
                mutationId,
                null,
                false));
    }

    [McpServerTool(
        Name = "file_delete",
        Title = "Recoverably delete workspace file",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Recoverably deletes one regular file by same-filesystem rename into private recovery storage. There is no permanent-delete API. The returned recoveryId can be used with file_restore. mutationId makes retries replay-safe.")]
    public static LifecycleMutationReceipt FileDelete(
        IWorkspaceLifecycleService lifecycleService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Stable unique id for this mutation/retry sequence.")] string mutationId,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative regular file path.")] string relativePath)
    {
        ArgumentNullException.ThrowIfNull(lifecycleService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        SessionAuthorization.RequireMutation(sessionGuard);

        var result = lifecycleService.DeleteFile(
                mutationId,
                workspaceId,
                relativePath);

        return ToolResultMapper.RequireValue(
            result,
            auditWriter,
            new MutationAuditContext(
                "file_delete",
                workspaceId,
                relativePath,
                null,
                mutationId,
                null,
                false));
    }

    [McpServerTool(
        Name = "file_restore",
        Title = "Restore recoverably deleted file",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Restores a recoverably deleted file using its recoveryId. Current workspace restore permission and path policy are revalidated, and existing destinations are never overwritten. mutationId makes retries replay-safe.")]
    public static LifecycleMutationReceipt FileRestore(
        IWorkspaceLifecycleService lifecycleService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Stable unique id for this mutation/retry sequence.")] string mutationId,
        [Description("Recovery id returned by file_delete.")] string recoveryId)
    {
        ArgumentNullException.ThrowIfNull(lifecycleService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        SessionAuthorization.RequireMutation(sessionGuard);

        var result = lifecycleService.RestoreFile(
                mutationId,
                recoveryId);

        return ToolResultMapper.RequireValue(
            result,
            auditWriter,
            new MutationAuditContext(
                "file_restore",
                null,
                null,
                null,
                mutationId,
                recoveryId,
                false));
    }
}
