using System.ComponentModel;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class FileQueryTools
{
    [McpServerTool(
        Name = "file_list",
        Title = "List workspace directory",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists a bounded page of entries inside an approved workspace directory. Paths are workspace-relative; absolute paths and policy-denied paths are rejected.")]
    public static DirectoryPage FileList(
        IWorkspaceQueryService queryService,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative directory path. Use an empty string for the workspace root.")] string relativePath = "",
        [Description("Zero-based entry offset for continuation.")] int offset = 0,
        [Description("Optional maximum entries for this page. Server configuration still caps the result.")] int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        SessionAuthorization.RequireRead(sessionGuard);
        return ToolResultMapper.RequireValue(
            queryService.ListDirectory(
                workspaceId,
                relativePath,
                offset,
                limit));
    }

    [McpServerTool(
        Name = "file_stat",
        Title = "Inspect workspace file metadata",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns metadata for one approved workspace-relative path after applying the Chunk 03 security policy.")]
    public static FileMetadata FileStat(
        IWorkspaceQueryService queryService,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative file or directory path.")] string relativePath)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        SessionAuthorization.RequireRead(sessionGuard);
        return ToolResultMapper.RequireValue(
            queryService.Stat(
                workspaceId,
                relativePath));
    }

    [McpServerTool(
        Name = "file_read",
        Title = "Read workspace text file",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Reads a bounded UTF-8 text segment from an approved workspace file and returns the complete-file SHA-256 for optimistic updates. Binary or unsupported text files are rejected.")]
    public static TextReadPage FileRead(
        IWorkspaceQueryService queryService,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Workspace-relative file path.")] string relativePath,
        [Description("Byte offset returned by a prior partial read; normally 0 for the first read.")] long byteOffset = 0,
        [Description("Optional maximum bytes for this response. Server configuration still caps the result.")] int? maxBytes = null)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        SessionAuthorization.RequireRead(sessionGuard);
        return ToolResultMapper.RequireValue(
            queryService.ReadText(
                workspaceId,
                relativePath,
                byteOffset,
                maxBytes));
    }

    [McpServerTool(
        Name = "file_search",
        Title = "Search workspace text",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Performs bounded literal text search inside an approved workspace. Returns relative paths, line numbers, and excerpts while honoring denied paths, configured search exclusions, depth, result, and timeout limits.")]
    public static SearchPage FileSearch(
        IWorkspaceQueryService queryService,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id.")] string workspaceId,
        [Description("Literal text to search for.")] string query,
        [Description("Workspace-relative directory to search from. Use an empty string for the workspace root.")] string relativePath = "",
        [Description("Whether matching is case-sensitive.")] bool caseSensitive = false,
        [Description("Optional maximum number of matches. Server configuration still caps the result.")] int? maxResults = null)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        SessionAuthorization.RequireRead(sessionGuard);
        return ToolResultMapper.RequireValue(
            queryService.Search(
                workspaceId,
                query,
                relativePath,
                caseSensitive,
                maxResults));
    }
}
