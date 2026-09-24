namespace LocalAgent.Core.Files;

public interface IWorkspaceMutationService
{
    MutationResult<FileMutationReceipt> CreateText(
        string workspaceId,
        string relativePath,
        string content,
        bool dryRun = false);

    MutationResult<FileMutationReceipt> UpdateText(
        string workspaceId,
        string relativePath,
        string content,
        string expectedHash,
        bool dryRun = false);
}
