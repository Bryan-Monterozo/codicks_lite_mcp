using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Files;

namespace LocalAgent.Infrastructure.Recovery;

public sealed class UpdateBackupManagementService(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver,
    IWorkspacePathPolicy pathPolicy,
    IFileHasher fileHasher,
    IAtomicFileWriter atomicFileWriter,
    IUpdateBackupStore backupStore) : IUpdateBackupManagementService
{
    private readonly string _recoveryRoot =
        Path.Combine(
            pathResolver.Resolve(
                configuration.Agent.StateDirectory),
            "recovery",
            "updates");

    public IReadOnlyList<UpdateBackupInfo> List()
    {
        if (!Directory.Exists(_recoveryRoot))
        {
            return Array.Empty<UpdateBackupInfo>();
        }

        return Directory
            .EnumerateFiles(
                _recoveryRoot,
                "*.json",
                SearchOption.AllDirectories)
            .Select(ReadMetadataFile)
            .OrderByDescending(
                item =>
                    item.CreatedAtUtc)
            .ThenBy(
                item =>
                    item.Id,
                StringComparer.Ordinal)
            .ToArray();
    }

    public UpdateBackupInfo GetBackup(
        string backupId)
    {
        ValidateBackupId(
            backupId);

        var metadataPath =
            FindMetadataPath(
                backupId);

        return ReadMetadataFile(
            metadataPath);
    }

    public UpdateBackupRestorePreview PreviewRestore(
        string backupId)
    {
        var backup =
            GetBackup(backupId);

        var access =
            pathPolicy.ValidateExisting(
                backup.WorkspaceId,
                backup.RelativePath,
                WorkspaceOperation.Update,
                allowWorkspaceRoot: false);

        if (!access.Allowed ||
            access.FullPath is null)
        {
            throw new InvalidOperationException(
                $"Restore access denied: {access.Message}");
        }

        if (access.EntryKind !=
            FileSystemEntryKind.RegularFile)
        {
            throw new InvalidOperationException(
                "Update backup restore requires an existing regular file.");
        }

        var bytes =
            File.ReadAllBytes(
                access.FullPath);

        return new UpdateBackupRestorePreview(
            backup,
            fileHasher.Compute(bytes),
            bytes.LongLength);
    }

    public MutationResult<UpdateBackupRestoreReceipt> Restore(
        string backupId,
        string expectedCurrentHash)
    {
        if (!MutationValidation.IsSha256(
                expectedCurrentHash))
        {
            return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                FileMutationError.InvalidRequest,
                "expectedCurrentHash must be a 64-character hexadecimal SHA-256 value.");
        }

        UpdateBackupInfo backup;

        try
        {
            backup =
                GetBackup(backupId);
        }
        catch (FileNotFoundException exception)
        {
            return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                FileMutationError.RecoveryNotFound,
                exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                FileMutationError.RecoveryUnavailable,
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                FileMutationError.InvalidRequest,
                exception.Message);
        }

        var initialAccess =
            pathPolicy.ValidateExisting(
                backup.WorkspaceId,
                backup.RelativePath,
                WorkspaceOperation.Update,
                allowWorkspaceRoot: false);

        if (!initialAccess.Allowed ||
            initialAccess.FullPath is null)
        {
            return FromAccessFailure<UpdateBackupRestoreReceipt>(
                initialAccess);
        }

        if (initialAccess.EntryKind !=
            FileSystemEntryKind.RegularFile)
        {
            return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                FileMutationError.NotAFile,
                "Update backup restore requires an existing regular file.");
        }

        var normalizedExpected =
            expectedCurrentHash
                .Trim()
                .ToUpperInvariant();

        byte[] backupBytes;

        try
        {
            backupBytes =
                ReadBackupBytes(
                    backup);
        }
        catch (IOException exception)
        {
            return RecoveryFailure(
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return RecoveryFailure(
                exception);
        }
        catch (InvalidDataException exception)
        {
            return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                FileMutationError.RecoveryUnavailable,
                exception.Message);
        }

        lock (MutationCoordinator.SyncRoot)
        {
            var access =
                pathPolicy.ValidateExisting(
                    backup.WorkspaceId,
                    backup.RelativePath,
                    WorkspaceOperation.Update,
                    allowWorkspaceRoot: false);

            if (!access.Allowed ||
                access.FullPath is null ||
                access.NormalizedRelativePath is null)
            {
                return FromAccessFailure<UpdateBackupRestoreReceipt>(
                    access);
            }

            if (access.EntryKind !=
                FileSystemEntryKind.RegularFile)
            {
                return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                    FileMutationError.NotAFile,
                    "Update backup restore requires an existing regular file.");
            }

            byte[] currentBytes;

            try
            {
                currentBytes =
                    File.ReadAllBytes(
                        access.FullPath);
            }
            catch (IOException exception)
            {
                return IoFailure<UpdateBackupRestoreReceipt>(
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<UpdateBackupRestoreReceipt>(
                    exception);
            }

            var currentHash =
                fileHasher.Compute(
                    currentBytes);

            if (!string.Equals(
                    currentHash,
                    normalizedExpected,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                    FileMutationError.Conflict,
                    "The current file changed after backup restore confirmation. Re-run backup-restore.");
            }

            UpdateBackupReceipt safetyBackup;

            try
            {
                safetyBackup =
                    backupStore.Save(
                        backup.WorkspaceId,
                        access.NormalizedRelativePath,
                        currentBytes,
                        currentHash);
            }
            catch (IOException exception)
            {
                return BackupFailure(
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return BackupFailure(
                    exception);
            }

            access =
                pathPolicy.ValidateExisting(
                    backup.WorkspaceId,
                    backup.RelativePath,
                    WorkspaceOperation.Update,
                    allowWorkspaceRoot: false);

            if (!access.Allowed ||
                access.FullPath is null)
            {
                return FromAccessFailure<UpdateBackupRestoreReceipt>(
                    access);
            }

            string hashBeforeReplace;

            try
            {
                hashBeforeReplace =
                    fileHasher.ComputeFile(
                        access.FullPath);
            }
            catch (IOException exception)
            {
                return IoFailure<UpdateBackupRestoreReceipt>(
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<UpdateBackupRestoreReceipt>(
                    exception);
            }

            if (!string.Equals(
                    hashBeforeReplace,
                    normalizedExpected,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                    FileMutationError.Conflict,
                    "The current file changed during backup restore. The destination was not replaced.");
            }

            UnixFileMode? unixFileMode =
                null;

            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    unixFileMode =
                        File.GetUnixFileMode(
                            access.FullPath);
                }

                atomicFileWriter.ReplaceExisting(
                    access.FullPath,
                    backupBytes,
                    unixFileMode);

                var restoredHash =
                    fileHasher.ComputeFile(
                        access.FullPath);

                if (!string.Equals(
                        restoredHash,
                        backup.OriginalSha256,
                        StringComparison.Ordinal))
                {
                    return MutationResult.Fail<UpdateBackupRestoreReceipt>(
                        FileMutationError.AtomicWriteFailed,
                        "Restored file hash does not match the selected update backup.");
                }

                return MutationResult.Ok(
                    new UpdateBackupRestoreReceipt(
                        backup.Id,
                        backup.WorkspaceId,
                        backup.RelativePath,
                        restoredHash,
                        backupBytes.LongLength,
                        safetyBackup.Id));
            }
            catch (IOException exception)
            {
                return AtomicWriteFailure(
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return AtomicWriteFailure(
                    exception);
            }
        }
    }

    private byte[] ReadBackupBytes(
        UpdateBackupInfo backup)
    {
        var metadataPath =
            FindMetadataPath(
                backup.Id);

        var directory =
            Path.GetDirectoryName(
                metadataPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidDataException(
                "Update backup metadata directory is invalid.");
        }

        var contentPath =
            Path.Combine(
                directory,
                $"{backup.Id}.bin");

        if (!File.Exists(
                contentPath))
        {
            throw new InvalidDataException(
                "Update backup payload is missing.");
        }

        var bytes =
            File.ReadAllBytes(
                contentPath);

        if (bytes.LongLength !=
            backup.SizeBytes)
        {
            throw new InvalidDataException(
                "Update backup payload size does not match metadata.");
        }

        var actualHash =
            fileHasher.Compute(
                bytes);

        if (!string.Equals(
                actualHash,
                backup.OriginalSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Update backup payload hash does not match metadata.");
        }

        return bytes;
    }

    private string FindMetadataPath(
        string backupId)
    {
        if (!Directory.Exists(
                _recoveryRoot))
        {
            throw new FileNotFoundException(
                $"Update backup '{backupId}' was not found.");
        }

        var matches =
            Directory
                .EnumerateFiles(
                    _recoveryRoot,
                    $"{backupId}.json",
                    SearchOption.AllDirectories)
                .Take(2)
                .ToArray();

        if (matches.Length == 0)
        {
            throw new FileNotFoundException(
                $"Update backup '{backupId}' was not found.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"Update backup id '{backupId}' is ambiguous.");
        }

        return matches[0];
    }

    private static UpdateBackupInfo ReadMetadataFile(
        string path)
    {
        try
        {
            var bytes =
                File.ReadAllBytes(
                    path);

            var metadata =
                JsonSerializer.Deserialize<BackupMetadata>(
                    bytes);

            if (metadata is null)
            {
                throw new InvalidDataException(
                    $"Invalid update backup metadata: {path}");
            }

            if (string.IsNullOrWhiteSpace(
                    metadata.Id) ||
                string.IsNullOrWhiteSpace(
                    metadata.WorkspaceId) ||
                string.IsNullOrWhiteSpace(
                    metadata.RelativePath) ||
                !MutationValidation.IsSha256(
                    metadata.OriginalSha256) ||
                metadata.SizeBytes < 0)
            {
                throw new InvalidDataException(
                    $"Invalid update backup metadata: {path}");
            }

            return new UpdateBackupInfo(
                metadata.Id,
                metadata.WorkspaceId,
                metadata.RelativePath,
                metadata.OriginalSha256
                    .Trim()
                    .ToUpperInvariant(),
                metadata.SizeBytes,
                metadata.CreatedAtUtc);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid update backup metadata: {path}",
                exception);
        }
    }

    private static void ValidateBackupId(
        string backupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            backupId);

        if (backupId.Length > 128 ||
            backupId.Any(
                character =>
                    !char.IsLetterOrDigit(
                        character) &&
                    character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "backupId contains unsupported characters.",
                nameof(backupId));
        }
    }

    private static MutationResult<T> FromAccessFailure<T>(
        PathPolicyResult result)
        where T : class
    {
        var error =
            result.Error switch
            {
                WorkspaceAccessError.PathNotFound =>
                    FileMutationError.NotFound,

                WorkspaceAccessError.InvalidRelativePath =>
                    FileMutationError.InvalidRequest,

                WorkspaceAccessError.InspectionFailed =>
                    FileMutationError.IoError,

                _ =>
                    FileMutationError.AccessDenied
            };

        return MutationResult.Fail<T>(
            error,
            result.Message,
            result.Error);
    }

    private static MutationResult<UpdateBackupRestoreReceipt> RecoveryFailure(
        Exception exception) =>
        MutationResult.Fail<UpdateBackupRestoreReceipt>(
            FileMutationError.RecoveryUnavailable,
            $"Update backup could not be read: {exception.Message}");

    private static MutationResult<UpdateBackupRestoreReceipt> BackupFailure(
        Exception exception) =>
        MutationResult.Fail<UpdateBackupRestoreReceipt>(
            FileMutationError.BackupFailed,
            $"Current file safety backup could not be saved; restore was not performed: {exception.Message}");

    private static MutationResult<T> IoFailure<T>(
        Exception exception)
        where T : class =>
        MutationResult.Fail<T>(
            FileMutationError.IoError,
            $"Backup restore filesystem preparation failed: {exception.Message}");

    private static MutationResult<UpdateBackupRestoreReceipt> AtomicWriteFailure(
        Exception exception) =>
        MutationResult.Fail<UpdateBackupRestoreReceipt>(
            FileMutationError.AtomicWriteFailed,
            $"Backup restore atomic replacement failed: {exception.Message}");

    private sealed record BackupMetadata(
        string Id,
        string WorkspaceId,
        string RelativePath,
        string OriginalSha256,
        long SizeBytes,
        DateTimeOffset CreatedAtUtc);
}
