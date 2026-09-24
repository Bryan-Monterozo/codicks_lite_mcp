using LocalAgent.Core.Security;

namespace LocalAgent.Core.Workspaces;

public sealed record WorkspaceResolutionResult(
    bool Resolved,
    WorkspaceDescriptor? Workspace,
    WorkspaceAccessError Error,
    string Message)
{
    public static WorkspaceResolutionResult Success(WorkspaceDescriptor workspace) =>
        new(true, workspace, WorkspaceAccessError.None, string.Empty);

    public static WorkspaceResolutionResult Failure(WorkspaceAccessError error, string message) =>
        new(false, null, error, message);
}
