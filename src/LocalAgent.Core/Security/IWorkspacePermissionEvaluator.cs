using LocalAgent.Core.Workspaces;

namespace LocalAgent.Core.Security;

public interface IWorkspacePermissionEvaluator
{
    PermissionDecision Evaluate(WorkspaceDescriptor workspace, WorkspaceOperation operation);
}
