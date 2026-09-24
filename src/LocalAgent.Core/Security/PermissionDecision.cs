namespace LocalAgent.Core.Security;

public sealed record PermissionDecision(
    bool Allowed,
    WorkspaceAccessError Error,
    string Message)
{
    public static PermissionDecision Allow() => new(true, WorkspaceAccessError.None, string.Empty);

    public static PermissionDecision Deny(WorkspaceAccessError error, string message) =>
        new(false, error, message);
}
