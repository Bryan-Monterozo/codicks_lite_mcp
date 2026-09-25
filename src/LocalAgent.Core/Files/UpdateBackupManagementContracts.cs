namespace LocalAgent.Core.Files;

public sealed record UpdateBackupInfo(
    string Id,
    string WorkspaceId,
    string RelativePath,
    string OriginalSha256,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record UpdateBackupRestorePreview(
    UpdateBackupInfo Backup,
    string CurrentSha256,
    long CurrentSizeBytes);

public sealed record UpdateBackupRestoreReceipt(
    string BackupId,
    string WorkspaceId,
    string RelativePath,
    string RestoredSha256,
    long SizeBytes,
    string SafetyBackupId);

public interface IUpdateBackupManagementService
{
    IReadOnlyList<UpdateBackupInfo> List();

    UpdateBackupInfo GetBackup(
        string backupId);

    UpdateBackupRestorePreview PreviewRestore(
        string backupId);

    MutationResult<UpdateBackupRestoreReceipt> Restore(
        string backupId,
        string expectedCurrentHash);
}
