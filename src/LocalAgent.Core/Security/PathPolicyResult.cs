using LocalAgent.Core.Workspaces;

namespace LocalAgent.Core.Security;

public sealed record PathPolicyResult(
    bool Allowed,
    WorkspaceAccessError Error,
    string Message,
    WorkspaceDescriptor? Workspace,
    string? NormalizedRelativePath,
    string? FullPath,
    FileSystemEntryKind EntryKind)
{
    public static PathPolicyResult Allow(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string fullPath,
        FileSystemEntryKind entryKind) =>
        new(
            true,
            WorkspaceAccessError.None,
            string.Empty,
            workspace,
            normalizedRelativePath,
            fullPath,
            entryKind);

    public static PathPolicyResult Deny(
        WorkspaceAccessError error,
        string message,
        WorkspaceDescriptor? workspace = null,
        string? normalizedRelativePath = null,
        string? fullPath = null,
        FileSystemEntryKind entryKind = FileSystemEntryKind.Unknown) =>
        new(false, error, message, workspace, normalizedRelativePath, fullPath, entryKind);
}
