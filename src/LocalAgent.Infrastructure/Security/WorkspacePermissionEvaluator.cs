using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Security;

public sealed class WorkspacePermissionEvaluator(AgentConfiguration configuration) : IWorkspacePermissionEvaluator
{
    private readonly bool _globalReadOnly = configuration.Agent.ReadOnly;

    public PermissionDecision Evaluate(WorkspaceDescriptor workspace, WorkspaceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (!workspace.Enabled)
        {
            return PermissionDecision.Deny(
                WorkspaceAccessError.WorkspaceDisabled,
                $"Workspace '{workspace.Id}' is disabled.");
        }

        if (_globalReadOnly && operation != WorkspaceOperation.Read)
        {
            return PermissionDecision.Deny(
                WorkspaceAccessError.GlobalReadOnly,
                "The agent is running in global read-only mode.");
        }

        if (!workspace.AllowedOperations.Contains(operation))
        {
            return PermissionDecision.Deny(
                WorkspaceAccessError.OperationDenied,
                $"Operation '{operation}' is not allowed for workspace '{workspace.Id}'.");
        }

        return PermissionDecision.Allow();
    }
}
