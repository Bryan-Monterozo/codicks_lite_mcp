namespace LocalAgent.Core.Workspaces;

public interface IWorkspaceResolver
{
    WorkspaceResolutionResult Resolve(string workspaceId);
}
