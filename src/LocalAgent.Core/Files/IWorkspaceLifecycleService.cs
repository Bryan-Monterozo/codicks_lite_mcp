namespace LocalAgent.Core.Files;

public interface IWorkspaceLifecycleService
{
    MutationResult<LifecycleMutationReceipt> CreateDirectory(
        string mutationId,
        string workspaceId,
        string relativePath);

    MutationResult<LifecycleMutationReceipt> MoveFile(
        string mutationId,
        string workspaceId,
        string sourceRelativePath,
        string destinationRelativePath,
        string expectedHash);

    MutationResult<LifecycleMutationReceipt> DeleteFile(
        string mutationId,
        string workspaceId,
        string relativePath);

    MutationResult<LifecycleMutationReceipt> RestoreFile(
        string mutationId,
        string recoveryId);
}
