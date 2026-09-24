using LocalAgent.Core.Workspaces;

namespace LocalAgent.Core.Security;

public interface IWorkspacePathPolicy
{
    PathPolicyResult ValidateExisting(
        string workspaceId,
        string relativePath,
        WorkspaceOperation operation,
        bool allowWorkspaceRoot = false);

    PathPolicyResult ValidateCreate(
        string workspaceId,
        string relativePath,
        WorkspaceOperation operation);

    MovePathPolicyResult ValidateMove(
        string workspaceId,
        string sourceRelativePath,
        string destinationRelativePath);
}
