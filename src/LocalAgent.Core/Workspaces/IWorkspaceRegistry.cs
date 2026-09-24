namespace LocalAgent.Core.Workspaces;

public interface IWorkspaceRegistry
{
    int Count { get; }

    IReadOnlyList<WorkspaceDescriptor> List();

    bool TryGet(string workspaceId, out WorkspaceDescriptor? workspace);
}
