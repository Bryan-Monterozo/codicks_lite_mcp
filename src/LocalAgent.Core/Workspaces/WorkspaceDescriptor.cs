namespace LocalAgent.Core.Workspaces;

public sealed record WorkspaceDescriptor(
    string Id,
    string Root,
    bool Enabled,
    IReadOnlyCollection<WorkspaceOperation> AllowedOperations);
