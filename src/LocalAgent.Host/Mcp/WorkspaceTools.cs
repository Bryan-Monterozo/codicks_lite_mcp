using System.ComponentModel;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class WorkspaceTools
{
    [McpServerTool(
        Name = "workspace_list",
        Title = "List approved workspaces",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Lists configured local workspaces approved for Codicks Lite, including whether each is enabled and its configured operation permissions. Does not modify the computer.")]
    public static WorkspaceListResponse WorkspaceList(
        IWorkspaceRegistry workspaceRegistry,
        ISessionGuard sessionGuard)
    {
        ArgumentNullException.ThrowIfNull(workspaceRegistry);
        SessionAuthorization.RequireRead(sessionGuard);

        var workspaces = workspaceRegistry.List()
            .Select(workspace => new WorkspaceToolSummary(
                workspace.Id,
                workspace.Root,
                workspace.Enabled,
                workspace.AllowedOperations
                    .Select(operation => operation.ToString())
                    .ToArray()))
            .ToArray();

        return new WorkspaceListResponse(workspaces);
    }

    [McpServerTool(
        Name = "workspace_inspect",
        Title = "Inspect approved workspace",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Inspects one approved workspace and its root metadata through the Chunk 03 security policy. The workspace is identified by configured workspaceId, never by an arbitrary absolute path.")]
    public static WorkspaceInspection WorkspaceInspect(
        IWorkspaceQueryService queryService,
        ISessionGuard sessionGuard,
        [Description("Configured workspace id from workspace_list.")] string workspaceId)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        SessionAuthorization.RequireRead(sessionGuard);
        return ToolResultMapper.RequireValue(
            queryService.InspectWorkspace(workspaceId));
    }
}
