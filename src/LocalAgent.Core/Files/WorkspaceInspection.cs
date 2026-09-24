using LocalAgent.Core.Workspaces;

namespace LocalAgent.Core.Files;

public sealed record WorkspaceInspection(
    string Id,
    string Root,
    bool Enabled,
    IReadOnlyList<WorkspaceOperation> AllowedOperations,
    FileMetadata RootEntry);
