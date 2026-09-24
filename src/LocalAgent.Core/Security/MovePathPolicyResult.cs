namespace LocalAgent.Core.Security;

public sealed record MovePathPolicyResult(
    bool Allowed,
    WorkspaceAccessError Error,
    string Message,
    string? SourceFullPath,
    string? DestinationFullPath,
    FileSystemEntryKind SourceKind)
{
    public static MovePathPolicyResult Allow(
        string sourceFullPath,
        string destinationFullPath,
        FileSystemEntryKind sourceKind) =>
        new(
            true,
            WorkspaceAccessError.None,
            string.Empty,
            sourceFullPath,
            destinationFullPath,
            sourceKind);

    public static MovePathPolicyResult Deny(
        WorkspaceAccessError error,
        string message,
        string? sourceFullPath = null,
        string? destinationFullPath = null,
        FileSystemEntryKind sourceKind = FileSystemEntryKind.Unknown) =>
        new(false, error, message, sourceFullPath, destinationFullPath, sourceKind);
}
