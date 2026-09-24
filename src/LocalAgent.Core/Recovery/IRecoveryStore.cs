using LocalAgent.Core.Workspaces;

namespace LocalAgent.Core.Recovery;

public interface IRecoveryStore
{
    RecoveryRecord PrepareDelete(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string sha256,
        long sizeBytes);

    RecoveryRecord? Load(string recoveryId);

    RecoveryRecord UpdateStatus(
        string recoveryId,
        RecoveryStatus status);
}
