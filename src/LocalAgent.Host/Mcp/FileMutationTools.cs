using System.ComponentModel;
using LocalAgent.Core.Audit;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Files;
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
    [Description("Backward-compatible complete-content replacement path. For localized edits, prefer file_patch_preview plus token-only file_patch_apply to avoid resending the full file. Uses the SHA-256 from a prior file_read, creates a recovery backup, and rejects stale hashes with CONFLICT.")]
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
        Name = "file_patch_apply",
        Title = "Apply reviewed workspace patch",
        UseStructuredContent = true,
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Applies the exact patch retained by file_patch_preview using its short-lived one-time review token. New clients may omit patch; the optional patch field remains accepted for v1.2.1 compatibility. Requires mutation authorization and workspace Update permission.")]
    public static FilePatchApplyResult FilePatchApply(
        IWorkspacePatchApplyService applyService,
        IAuditWriter auditWriter,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id used during preview.")] string workspaceId,
        [Description("Workspace-relative file path used during preview.")] string relativePath,
        [Description("Reviewed base SHA-256 used during preview.")] string expectedHash,
        [Description("Short-lived review token returned by file_patch_preview.")] string reviewToken,
        [Description("Optional exact patch text for v1.2.1 compatibility. New clients should omit this and let Codicks use the memory-only patch bound to reviewToken.")] string? patch = null)
    {
        ArgumentNullException.ThrowIfNull(applyService);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(sessionGuard);

        var auditContext =
            new FileReviewAuditContext(
                "file_patch_apply",
                workspaceId,
                relativePath,
                FileReviewAuditMetadata.NormalizeSha256(
                    expectedHash),
                ProposedSha256: null,
                PatchSha256:
                    FileReviewAuditMetadata.ComputeUtf8Sha256(
                        patch),
                Preview: false,
                BackupId: null);

        ToolResultMapper.RequireReviewAuthorization(
            sessionGuard.AuthorizeMutation(),
            auditWriter,
            auditContext);

        var result = applyService.Apply(
            workspaceId,
            relativePath,
            patch,
            expectedHash,
            reviewToken);

        var receipt =
            ToolResultMapper.RequireValue(
                result,
                auditWriter,
                auditContext);

        return new FilePatchApplyResult(
            receipt.RelativePath,
            receipt.SizeBytes,
            receipt.Sha256,
            receipt.Encoding,
            receipt.LineEnding,
            receipt.BackupId,
            ApplyState: "applied",
            ApplySummary:
                FileReviewSummaryFormatter.CreateApplySummary(
                    receipt));
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
