using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Files;

public sealed class WorkspaceMutationService(
    IWorkspacePathPolicy pathPolicy,
    AgentConfiguration configuration,
    IFileHasher fileHasher,
    IAtomicFileWriter atomicFileWriter,
    IUpdateBackupStore backupStore) : IWorkspaceMutationService
{
    private readonly long _maxEditableFileBytes =
        configuration.Agent.Limits.MaxEditableFileBytes;

    public MutationResult<FileMutationReceipt> CreateText(
        string workspaceId,
        string relativePath,
        string content,
        bool dryRun = false)
    {
        var initialAccess = pathPolicy.ValidateCreate(
            workspaceId,
            relativePath,
            WorkspaceOperation.Create);

        if (!initialAccess.Allowed)
        {
            return FromAccessFailure<FileMutationReceipt>(initialAccess);
        }

        var prepared = TextFileCodec.PrepareCreate(
            content,
            _maxEditableFileBytes);

        if (!prepared.Success)
        {
            return PreparedTextFailure(prepared.Message);
        }

        lock (MutationCoordinator.SyncRoot)
        {
            var access = pathPolicy.ValidateCreate(
                workspaceId,
                relativePath,
                WorkspaceOperation.Create);

            if (!access.Allowed ||
                access.FullPath is null ||
                access.NormalizedRelativePath is null)
            {
                return FromAccessFailure<FileMutationReceipt>(access);
            }

            var projectedHash = fileHasher.Compute(prepared.Bytes);
            var projected = new FileMutationReceipt(
                access.NormalizedRelativePath,
                prepared.Bytes.LongLength,
                projectedHash,
                prepared.Encoding,
                prepared.LineEnding,
                dryRun,
                BackupId: null);

            if (dryRun)
            {
                return MutationResult.Ok(projected);
            }

            // Revalidate immediately before touching the filesystem. The final
            // File.Move in AtomicFileWriter remains the no-overwrite race guard.
            access = pathPolicy.ValidateCreate(
                workspaceId,
                relativePath,
                WorkspaceOperation.Create);

            if (!access.Allowed || access.FullPath is null)
            {
                return FromAccessFailure<FileMutationReceipt>(access);
            }

            try
            {
                atomicFileWriter.CreateNew(access.FullPath, prepared.Bytes);
                var actualHash = fileHasher.ComputeFile(access.FullPath);

                return MutationResult.Ok(projected with
                {
                    Sha256 = actualHash,
                    DryRun = false
                });
            }
            catch (IOException exception) when (File.Exists(access.FullPath))
            {
                return MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.AlreadyExists,
                    $"Create refused because the destination now exists: {exception.Message}");
            }
            catch (IOException exception)
            {
                return AtomicWriteFailure(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return AtomicWriteFailure(exception);
            }
        }
    }

    public MutationResult<FileMutationReceipt> UpdateText(
        string workspaceId,
        string relativePath,
        string content,
        string expectedHash,
        bool dryRun = false)
    {
        if (!MutationValidation.IsSha256(expectedHash))
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.InvalidRequest,
                "expectedHash must be a 64-character hexadecimal SHA-256 value.");
        }

        var initialAccess = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Update,
            allowWorkspaceRoot: false);

        if (!initialAccess.Allowed)
        {
            return FromAccessFailure<FileMutationReceipt>(initialAccess);
        }

        if (initialAccess.EntryKind != FileSystemEntryKind.RegularFile)
        {
            return MutationResult.Fail<FileMutationReceipt>(
                FileMutationError.NotAFile,
                "Only regular text files can be updated.");
        }

        lock (MutationCoordinator.SyncRoot)
        {
            var access = pathPolicy.ValidateExisting(
                workspaceId,
                relativePath,
                WorkspaceOperation.Update,
                allowWorkspaceRoot: false);

            if (!access.Allowed ||
                access.FullPath is null ||
                access.NormalizedRelativePath is null)
            {
                return FromAccessFailure<FileMutationReceipt>(access);
            }

            if (access.EntryKind != FileSystemEntryKind.RegularFile)
            {
                return MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.NotAFile,
                    "Only regular text files can be updated.");
            }

            byte[] currentBytes;
            try
            {
                var fileInfo = new FileInfo(access.FullPath);
                if (fileInfo.Length > _maxEditableFileBytes)
                {
                    return MutationResult.Fail<FileMutationReceipt>(
                        FileMutationError.FileTooLarge,
                        $"Existing file exceeds the configured MaxEditableFileBytes limit of {_maxEditableFileBytes} bytes.");
                }

                currentBytes = File.ReadAllBytes(access.FullPath);
            }
            catch (IOException exception)
            {
                return IoFailure<FileMutationReceipt>(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<FileMutationReceipt>(exception);
            }

            var normalizedExpectedHash = expectedHash.Trim().ToUpperInvariant();
            var currentHash = fileHasher.Compute(currentBytes);

            if (!string.Equals(
                    currentHash,
                    normalizedExpectedHash,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.Conflict,
                    "The file changed since it was read. Re-read it and retry with the new SHA-256 hash.");
            }

            var prepared = TextFileCodec.PrepareUpdate(
                currentBytes,
                content,
                _maxEditableFileBytes);

            if (!prepared.Success)
            {
                return PreparedTextFailure(prepared.Message);
            }

            var projectedHash = fileHasher.Compute(prepared.Bytes);
            var projected = new FileMutationReceipt(
                access.NormalizedRelativePath,
                prepared.Bytes.LongLength,
                projectedHash,
                prepared.Encoding,
                prepared.LineEnding,
                dryRun,
                BackupId: null);

            if (dryRun)
            {
                return MutationResult.Ok(projected);
            }

            UpdateBackupReceipt backup;
            try
            {
                backup = backupStore.Save(
                    workspaceId,
                    access.NormalizedRelativePath,
                    currentBytes,
                    currentHash);
            }
            catch (IOException exception)
            {
                return BackupFailure(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return BackupFailure(exception);
            }

            // Revalidate and re-hash immediately before replacement. This catches
            // changes made after the initial read but before the backup completed.
            access = pathPolicy.ValidateExisting(
                workspaceId,
                relativePath,
                WorkspaceOperation.Update,
                allowWorkspaceRoot: false);

            if (!access.Allowed || access.FullPath is null)
            {
                return FromAccessFailure<FileMutationReceipt>(access);
            }

            string currentHashBeforeReplace;
            try
            {
                currentHashBeforeReplace = fileHasher.ComputeFile(access.FullPath);
            }
            catch (IOException exception)
            {
                return IoFailure<FileMutationReceipt>(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<FileMutationReceipt>(exception);
            }

            if (!string.Equals(
                    currentHashBeforeReplace,
                    normalizedExpectedHash,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<FileMutationReceipt>(
                    FileMutationError.Conflict,
                    "The file changed during the update workflow. The destination was not replaced.");
            }

            UnixFileMode? unixFileMode = null;
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    unixFileMode = File.GetUnixFileMode(access.FullPath);
                }

                atomicFileWriter.ReplaceExisting(
                    access.FullPath,
                    prepared.Bytes,
                    unixFileMode);

                var actualHash = fileHasher.ComputeFile(access.FullPath);

                return MutationResult.Ok(projected with
                {
                    Sha256 = actualHash,
                    DryRun = false,
                    BackupId = backup.Id
                });
            }
            catch (IOException exception)
            {
                return AtomicWriteFailure(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return AtomicWriteFailure(exception);
            }
        }
    }

    private static MutationResult<T> FromAccessFailure<T>(PathPolicyResult result)
        where T : class
    {
        var error = result.Error switch
        {
            WorkspaceAccessError.PathAlreadyExists => FileMutationError.AlreadyExists,
            WorkspaceAccessError.PathNotFound => FileMutationError.NotFound,
            WorkspaceAccessError.ParentNotFound => FileMutationError.NotFound,
            WorkspaceAccessError.InvalidRelativePath => FileMutationError.InvalidRequest,
            WorkspaceAccessError.WorkspaceRootNotAllowed => FileMutationError.InvalidRequest,
            WorkspaceAccessError.InspectionFailed => FileMutationError.IoError,
            _ => FileMutationError.AccessDenied
        };

        return MutationResult.Fail<T>(
            error,
            result.Message,
            result.Error);
    }

    private static MutationResult<FileMutationReceipt> PreparedTextFailure(
        string message)
    {
        var error = message.Contains(
                "MaxEditableFileBytes",
                StringComparison.Ordinal)
            ? FileMutationError.FileTooLarge
            : FileMutationError.UnsupportedTextEncoding;

        return MutationResult.Fail<FileMutationReceipt>(
            error,
            message);
    }

    private static MutationResult<FileMutationReceipt> BackupFailure(
        Exception exception) =>
        MutationResult.Fail<FileMutationReceipt>(
            FileMutationError.BackupFailed,
            $"The recovery backup could not be saved; the destination was not changed: {exception.Message}");

    private static MutationResult<T> IoFailure<T>(Exception exception)
        where T : class =>
        MutationResult.Fail<T>(
            FileMutationError.IoError,
            $"Filesystem mutation preparation failed: {exception.Message}");

    private static MutationResult<FileMutationReceipt> AtomicWriteFailure(
        Exception exception) =>
        MutationResult.Fail<FileMutationReceipt>(
            FileMutationError.AtomicWriteFailed,
            $"Atomic file replacement failed: {exception.Message}");
}
