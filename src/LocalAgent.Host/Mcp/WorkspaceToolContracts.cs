namespace LocalAgent.Host.Mcp;

public sealed record WorkspaceListResponse(
    IReadOnlyList<WorkspaceToolSummary> Workspaces);

public sealed record WorkspaceToolSummary(
    string Id,
    string Root,
    bool Enabled,
    IReadOnlyList<string> AllowedOperations);
