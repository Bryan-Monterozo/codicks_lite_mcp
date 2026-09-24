namespace LocalAgent.Core.Files;

public interface IWorkspaceDiffService
{
    QueryResult<FileDiffResult> DiffText(
        string workspaceId,
        string relativePath,
        string content,
        string? expectedHash = null);
}
