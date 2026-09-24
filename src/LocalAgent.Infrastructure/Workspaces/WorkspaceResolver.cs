using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Workspaces;

public sealed class WorkspaceResolver(IWorkspaceRegistry workspaceRegistry) : IWorkspaceResolver
{
    public WorkspaceResolutionResult Resolve(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return WorkspaceResolutionResult.Failure(
                WorkspaceAccessError.InvalidWorkspaceId,
                "Workspace id is required.");
        }

        if (!workspaceRegistry.TryGet(workspaceId, out var workspace) || workspace is null)
        {
            return WorkspaceResolutionResult.Failure(
                WorkspaceAccessError.WorkspaceNotFound,
                $"Workspace '{workspaceId}' is not configured.");
        }

        if (!workspace.Enabled)
        {
            return WorkspaceResolutionResult.Failure(
                WorkspaceAccessError.WorkspaceDisabled,
                $"Workspace '{workspace.Id}' is disabled.");
        }

        return WorkspaceResolutionResult.Success(workspace);
    }
}
