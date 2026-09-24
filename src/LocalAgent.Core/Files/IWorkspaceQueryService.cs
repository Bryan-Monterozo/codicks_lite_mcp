namespace LocalAgent.Core.Files;

public interface IWorkspaceQueryService
{
    QueryResult<WorkspaceInspection> InspectWorkspace(string workspaceId);

    QueryResult<FileMetadata> Stat(string workspaceId, string relativePath);

    QueryResult<DirectoryPage> ListDirectory(
        string workspaceId,
        string relativePath,
        int offset = 0,
        int? limit = null);

    QueryResult<TextReadPage> ReadText(
        string workspaceId,
        string relativePath,
        long byteOffset = 0,
        int? maxBytes = null);

    QueryResult<SearchPage> Search(
        string workspaceId,
        string query,
        string relativePath = "",
        bool caseSensitive = false,
        int? maxResults = null);
}
