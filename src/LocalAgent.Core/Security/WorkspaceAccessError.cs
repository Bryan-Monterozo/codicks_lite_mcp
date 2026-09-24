namespace LocalAgent.Core.Security;

public enum WorkspaceAccessError
{
    None,
    InvalidWorkspaceId,
    WorkspaceNotFound,
    WorkspaceDisabled,
    GlobalReadOnly,
    OperationDenied,
    InvalidRelativePath,
    PathOutsideWorkspace,
    WorkspaceRootNotAllowed,
    PathDeniedByPolicy,
    PathNotFound,
    ParentNotFound,
    PathAlreadyExists,
    SymbolicLinkNotAllowed,
    HardLinkNotAllowed,
    UnsupportedEntryType,
    InspectionFailed
}
