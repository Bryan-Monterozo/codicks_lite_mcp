namespace LocalAgent.Core.Files;

public interface IUpdateBackupStore
{
    UpdateBackupReceipt Save(
        string workspaceId,
        string relativePath,
        byte[] originalBytes,
        string originalSha256);
}
