using System.Text;
using LocalAgent.Core.Files;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Files;

public sealed class WorkspaceLifecycleService(
    IWorkspacePathPolicy pathPolicy,
    IWorkspaceResolver workspaceResolver,
    IFileHasher fileHasher,
    IRecoveryStore recoveryStore,
    IMutationReceiptStore receiptStore) : IWorkspaceLifecycleService
{
    private const string CreateDirectoryOperation = "directory-create";
    private const string MoveOperation = "file-move";
    private const string DeleteOperation = "file-delete";
    private const string RestoreOperation = "file-restore";


    public MutationResult<LifecycleMutationReceipt> CreateDirectory(
        string mutationId,
        string workspaceId,
        string relativePath)
    {
        var mutationValidation = ValidateMutationId(mutationId);
        if (mutationValidation is not null)
        {
            return mutationValidation;
        }

        var fingerprint = CreateFingerprint(
            CreateDirectoryOperation,
            workspaceId,
            relativePath);

        lock (MutationCoordinator.SyncRoot)
        {
            var existing = receiptStore.Load(mutationId);
            var existingResult = ValidateExistingReceipt(
                existing,
                mutationId,
                fingerprint);

            if (existingResult is not null)
            {
                return existingResult;
            }

            if (existing is not null)
            {
                var reconciled = TryReconcileDirectory(existing);
                if (reconciled is not null)
                {
                    return reconciled;
                }
            }

            var preflight = pathPolicy.ValidateCreate(
                workspaceId,
                relativePath,
                WorkspaceOperation.Create);

            if (!preflight.Allowed &&
                preflight.Error != WorkspaceAccessError.ParentNotFound)
            {
                return FromAccessFailure<LifecycleMutationReceipt>(preflight);
            }

            if (preflight.NormalizedRelativePath is null)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.InvalidRequest,
                    "Directory path could not be normalized safely.");
            }

            var normalizedPath = preflight.NormalizedRelativePath;

            if (existing is null)
            {
                existing = NewStartedReceipt(
                    mutationId,
                    CreateDirectoryOperation,
                    fingerprint,
                    workspaceId,
                    sourceRelativePath: normalizedPath,
                    destinationRelativePath: normalizedPath,
                    recoveryId: null,
                    sha256: null);

                receiptStore.Create(existing);
            }

            var createResult = EnsureDirectoryChain(
                workspaceId,
                normalizedPath);

            if (!createResult.Success)
            {
                return createResult;
            }

            return Complete(
                existing,
                reconciled: false);
        }
    }

    public MutationResult<LifecycleMutationReceipt> MoveFile(
        string mutationId,
        string workspaceId,
        string sourceRelativePath,
        string destinationRelativePath,
        string expectedHash)
    {
        var mutationValidation = ValidateMutationId(mutationId);
        if (mutationValidation is not null)
        {
            return mutationValidation;
        }

        if (!MutationValidation.IsSha256(expectedHash))
        {
            return MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.InvalidRequest,
                "expectedHash must be a 64-character hexadecimal SHA-256 value.");
        }

        var normalizedExpectedHash = expectedHash.Trim().ToUpperInvariant();
        var fingerprint = CreateFingerprint(
            MoveOperation,
            workspaceId,
            sourceRelativePath,
            destinationRelativePath,
            normalizedExpectedHash);

        lock (MutationCoordinator.SyncRoot)
        {
            var existing = receiptStore.Load(mutationId);
            var existingResult = ValidateExistingReceipt(
                existing,
                mutationId,
                fingerprint);

            if (existingResult is not null)
            {
                return existingResult;
            }

            if (existing is not null)
            {
                var reconciled = TryReconcileMove(existing);
                if (reconciled is not null)
                {
                    return reconciled;
                }
            }

            var access = pathPolicy.ValidateMove(
                workspaceId,
                sourceRelativePath,
                destinationRelativePath);

            if (!access.Allowed ||
                access.SourceFullPath is null ||
                access.DestinationFullPath is null)
            {
                return FromMoveAccessFailure<LifecycleMutationReceipt>(access);
            }

            if (access.SourceKind != FileSystemEntryKind.RegularFile)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.NotAFile,
                    "Chunk 06 move/rename supports regular files only.");
            }

            var sourceHash = TryComputeFileHash(access.SourceFullPath);
            if (!sourceHash.Success || sourceHash.Value is null)
            {
                return sourceHash.ConvertFailure<LifecycleMutationReceipt>();
            }

            if (!string.Equals(
                    sourceHash.Value,
                    normalizedExpectedHash,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.Conflict,
                    "The move source changed since it was read. Re-read it and retry with the new SHA-256 hash.");
            }

            var resolution = workspaceResolver.Resolve(workspaceId);
            if (!resolution.Resolved || resolution.Workspace is null)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.AccessDenied,
                    resolution.Message,
                    resolution.Error);
            }

            var sourceNormalized = Path.GetRelativePath(
                resolution.Workspace.Root,
                access.SourceFullPath);
            var destinationNormalized = Path.GetRelativePath(
                resolution.Workspace.Root,
                access.DestinationFullPath);

            if (existing is null)
            {
                existing = NewStartedReceipt(
                    mutationId,
                    MoveOperation,
                    fingerprint,
                    workspaceId,
                    sourceNormalized,
                    destinationNormalized,
                    recoveryId: null,
                    sha256: normalizedExpectedHash);

                receiptStore.Create(existing);
            }

            // Final policy + hash check immediately before the rename.
            access = pathPolicy.ValidateMove(
                workspaceId,
                sourceRelativePath,
                destinationRelativePath);

            if (!access.Allowed ||
                access.SourceFullPath is null ||
                access.DestinationFullPath is null)
            {
                return FromMoveAccessFailure<LifecycleMutationReceipt>(access);
            }

            sourceHash = TryComputeFileHash(access.SourceFullPath);
            if (!sourceHash.Success || sourceHash.Value is null)
            {
                return sourceHash.ConvertFailure<LifecycleMutationReceipt>();
            }

            if (!string.Equals(
                    sourceHash.Value,
                    normalizedExpectedHash,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.Conflict,
                    "The move source changed during the move workflow. No rename was performed.");
            }

            try
            {
                File.Move(
                    access.SourceFullPath,
                    access.DestinationFullPath);
            }
            catch (IOException exception) when (File.Exists(access.DestinationFullPath))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.AlreadyExists,
                    $"Move refused because the destination exists: {exception.Message}");
            }
            catch (IOException exception)
            {
                return IoFailure<LifecycleMutationReceipt>(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<LifecycleMutationReceipt>(exception);
            }

            var destinationHash = TryComputeFileHash(access.DestinationFullPath);
            if (!destinationHash.Success || destinationHash.Value is null)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "The file was moved but its destination could not be verified. Retry with the same mutationId for reconciliation.");
            }

            if (!string.Equals(
                    destinationHash.Value,
                    normalizedExpectedHash,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "The file was moved but the destination hash does not match the expected source hash.");
            }

            return Complete(existing, reconciled: false);
        }
    }

    public MutationResult<LifecycleMutationReceipt> DeleteFile(
        string mutationId,
        string workspaceId,
        string relativePath)
    {
        var mutationValidation = ValidateMutationId(mutationId);
        if (mutationValidation is not null)
        {
            return mutationValidation;
        }

        var fingerprint = CreateFingerprint(
            DeleteOperation,
            workspaceId,
            relativePath);

        lock (MutationCoordinator.SyncRoot)
        {
            var existing = receiptStore.Load(mutationId);
            var existingResult = ValidateExistingReceipt(
                existing,
                mutationId,
                fingerprint);

            if (existingResult is not null)
            {
                return existingResult;
            }

            RecoveryRecord recovery;

            if (existing is not null)
            {
                if (string.IsNullOrWhiteSpace(existing.RecoveryId))
                {
                    return MutationResult.Fail<LifecycleMutationReceipt>(
                        FileMutationError.MutationStateConflict,
                        "The persisted delete receipt is missing its recovery id.");
                }

                var loadedRecovery = recoveryStore.Load(existing.RecoveryId);
                if (loadedRecovery is null)
                {
                    return MutationResult.Fail<LifecycleMutationReceipt>(
                        FileMutationError.RecoveryNotFound,
                        "The persisted delete receipt refers to missing recovery metadata.");
                }

                recovery = loadedRecovery;

                var reconciled = TryReconcileDelete(existing, recovery);
                if (reconciled is not null)
                {
                    return reconciled;
                }
            }
            else
            {
                var access = pathPolicy.ValidateExisting(
                    workspaceId,
                    relativePath,
                    WorkspaceOperation.Delete,
                    allowWorkspaceRoot: false);

                if (!access.Allowed ||
                    access.FullPath is null ||
                    access.NormalizedRelativePath is null ||
                    access.Workspace is null)
                {
                    return FromAccessFailure<LifecycleMutationReceipt>(access);
                }

                if (access.EntryKind != FileSystemEntryKind.RegularFile)
                {
                    return MutationResult.Fail<LifecycleMutationReceipt>(
                        FileMutationError.NotAFile,
                        "Chunk 06 recoverable delete supports regular files only; recursive directory deletion is not exposed.");
                }

                string sha256;
                long sizeBytes;

                try
                {
                    sha256 = fileHasher.ComputeFile(access.FullPath);
                    sizeBytes = new FileInfo(access.FullPath).Length;
                    recovery = recoveryStore.PrepareDelete(
                        access.Workspace,
                        access.NormalizedRelativePath,
                        sha256,
                        sizeBytes);
                }
                catch (IOException exception)
                {
                    return MutationResult.Fail<LifecycleMutationReceipt>(
                        FileMutationError.RecoveryUnavailable,
                        $"Recovery storage could not be prepared; delete was refused: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    return MutationResult.Fail<LifecycleMutationReceipt>(
                        FileMutationError.RecoveryUnavailable,
                        $"Recovery storage could not be prepared; delete was refused: {exception.Message}");
                }

                existing = NewStartedReceipt(
                    mutationId,
                    DeleteOperation,
                    fingerprint,
                    workspaceId,
                    access.NormalizedRelativePath,
                    destinationRelativePath: null,
                    recoveryId: recovery.Id,
                    sha256: sha256);

                receiptStore.Create(existing);
            }

            var finalAccess = pathPolicy.ValidateExisting(
                workspaceId,
                existing.SourceRelativePath ?? relativePath,
                WorkspaceOperation.Delete,
                allowWorkspaceRoot: false);

            if (!finalAccess.Allowed || finalAccess.FullPath is null)
            {
                return FromAccessFailure<LifecycleMutationReceipt>(finalAccess);
            }

            if (finalAccess.EntryKind != FileSystemEntryKind.RegularFile)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.NotAFile,
                    "Only regular files can be quarantined by Chunk 06 delete.");
            }

            var finalHash = TryComputeFileHash(finalAccess.FullPath);
            if (!finalHash.Success || finalHash.Value is null)
            {
                return finalHash.ConvertFailure<LifecycleMutationReceipt>();
            }

            if (!string.Equals(
                    finalHash.Value,
                    recovery.Sha256,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.Conflict,
                    "The file changed while delete recovery was being prepared. The file was not moved.");
            }

            if (File.Exists(recovery.QuarantinePath) || Directory.Exists(recovery.QuarantinePath))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "The recovery quarantine destination already exists unexpectedly.");
            }

            try
            {
                File.Move(finalAccess.FullPath, recovery.QuarantinePath);
            }
            catch (IOException exception)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.RecoveryUnavailable,
                    $"The file could not be moved atomically into recovery storage: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.RecoveryUnavailable,
                    $"The file could not be moved into recovery storage: {exception.Message}");
            }

            var quarantineHash = TryComputeFileHash(recovery.QuarantinePath);
            if (!quarantineHash.Success || quarantineHash.Value is null ||
                !string.Equals(
                    quarantineHash.Value,
                    recovery.Sha256,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "The file entered recovery storage but could not be verified. Retry with the same mutationId for reconciliation.");
            }

            try
            {
                recoveryStore.UpdateStatus(
                    recovery.Id,
                    RecoveryStatus.Quarantined);
            }
            catch (IOException exception)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    $"The file is quarantined but recovery metadata could not be finalized. Retry with the same mutationId: {exception.Message}");
            }

            return Complete(existing, reconciled: false);
        }
    }

    public MutationResult<LifecycleMutationReceipt> RestoreFile(
        string mutationId,
        string recoveryId)
    {
        var mutationValidation = ValidateMutationId(mutationId);
        if (mutationValidation is not null)
        {
            return mutationValidation;
        }

        if (string.IsNullOrWhiteSpace(recoveryId))
        {
            return MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.InvalidRequest,
                "recoveryId is required.");
        }

        var fingerprint = CreateFingerprint(
            RestoreOperation,
            recoveryId);

        lock (MutationCoordinator.SyncRoot)
        {
            var existing = receiptStore.Load(mutationId);
            var existingResult = ValidateExistingReceipt(
                existing,
                mutationId,
                fingerprint);

            if (existingResult is not null)
            {
                return existingResult;
            }

            var recovery = recoveryStore.Load(recoveryId);
            if (recovery is null)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.RecoveryNotFound,
                    "Recovery id was not found.");
            }

            if (existing is not null)
            {
                var reconciled = TryReconcileRestore(existing, recovery);
                if (reconciled is not null)
                {
                    return reconciled;
                }
            }

            if (recovery.Status == RecoveryStatus.Restored)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.RecoveryNotFound,
                    "This recovery record has already been restored.");
            }

            if (recovery.Status != RecoveryStatus.Quarantined)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "Recovery content is not in a restorable quarantined state.");
            }

            var access = pathPolicy.ValidateCreate(
                recovery.WorkspaceId,
                recovery.OriginalRelativePath,
                WorkspaceOperation.Restore);

            if (!access.Allowed ||
                access.FullPath is null ||
                access.NormalizedRelativePath is null)
            {
                return FromAccessFailure<LifecycleMutationReceipt>(access);
            }

            var quarantineHash = TryComputeFileHash(recovery.QuarantinePath);
            if (!quarantineHash.Success || quarantineHash.Value is null)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.RecoveryNotFound,
                    "Recovery content is missing or unreadable.");
            }

            if (!string.Equals(
                    quarantineHash.Value,
                    recovery.Sha256,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "Recovery content hash does not match its metadata.");
            }

            if (existing is null)
            {
                existing = NewStartedReceipt(
                    mutationId,
                    RestoreOperation,
                    fingerprint,
                    recovery.WorkspaceId,
                    sourceRelativePath: recovery.OriginalRelativePath,
                    destinationRelativePath: recovery.OriginalRelativePath,
                    recoveryId: recovery.Id,
                    sha256: recovery.Sha256);

                receiptStore.Create(existing);
            }

            // Recheck policy immediately before restore.
            access = pathPolicy.ValidateCreate(
                recovery.WorkspaceId,
                recovery.OriginalRelativePath,
                WorkspaceOperation.Restore);

            if (!access.Allowed || access.FullPath is null)
            {
                return FromAccessFailure<LifecycleMutationReceipt>(access);
            }

            try
            {
                File.Move(recovery.QuarantinePath, access.FullPath);
            }
            catch (IOException exception) when (File.Exists(access.FullPath))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.AlreadyExists,
                    $"Restore refused because the original path now exists: {exception.Message}");
            }
            catch (IOException exception)
            {
                return IoFailure<LifecycleMutationReceipt>(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<LifecycleMutationReceipt>(exception);
            }

            var restoredHash = TryComputeFileHash(access.FullPath);
            if (!restoredHash.Success || restoredHash.Value is null ||
                !string.Equals(
                    restoredHash.Value,
                    recovery.Sha256,
                    StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "The file was restored but could not be verified. Retry with the same mutationId for reconciliation.");
            }

            try
            {
                recoveryStore.UpdateStatus(
                    recovery.Id,
                    RecoveryStatus.Restored);
            }
            catch (IOException exception)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    $"The file was restored but recovery metadata could not be finalized. Retry with the same mutationId: {exception.Message}");
            }

            return Complete(existing, reconciled: false);
        }
    }

    private MutationResult<LifecycleMutationReceipt>? TryReconcileDirectory(
        PersistentMutationReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.DestinationRelativePath))
        {
            return MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.MutationStateConflict,
                "The directory-create receipt is missing its destination path.");
        }

        var access = pathPolicy.ValidateExisting(
            receipt.WorkspaceId,
            receipt.DestinationRelativePath,
            WorkspaceOperation.Create,
            allowWorkspaceRoot: false);

        if (access.Allowed && access.EntryKind == FileSystemEntryKind.Directory)
        {
            return Complete(receipt, reconciled: true);
        }

        if (access.Error == WorkspaceAccessError.PathNotFound)
        {
            return null;
        }

        return FromAccessFailure<LifecycleMutationReceipt>(access);
    }

    private MutationResult<LifecycleMutationReceipt>? TryReconcileMove(
        PersistentMutationReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.SourceRelativePath) ||
            string.IsNullOrWhiteSpace(receipt.DestinationRelativePath) ||
            string.IsNullOrWhiteSpace(receipt.Sha256))
        {
            return MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.MutationStateConflict,
                "The move receipt is incomplete.");
        }

        var source = pathPolicy.ValidateExisting(
            receipt.WorkspaceId,
            receipt.SourceRelativePath,
            WorkspaceOperation.Move,
            allowWorkspaceRoot: false);

        var destination = pathPolicy.ValidateExisting(
            receipt.WorkspaceId,
            receipt.DestinationRelativePath,
            WorkspaceOperation.Move,
            allowWorkspaceRoot: false);

        var sourceExists = source.Allowed;
        var destinationExists = destination.Allowed;

        if (!sourceExists &&
            source.Error == WorkspaceAccessError.PathNotFound &&
            destinationExists &&
            destination.EntryKind == FileSystemEntryKind.RegularFile &&
            destination.FullPath is not null)
        {
            var hash = TryComputeFileHash(destination.FullPath);
            if (hash.Success &&
                string.Equals(hash.Value, receipt.Sha256, StringComparison.Ordinal))
            {
                return Complete(receipt, reconciled: true);
            }
        }

        if (sourceExists &&
            source.EntryKind == FileSystemEntryKind.RegularFile &&
            !destinationExists &&
            destination.Error == WorkspaceAccessError.PathNotFound)
        {
            return null;
        }

        return MutationResult.Fail<LifecycleMutationReceipt>(
            FileMutationError.MutationStateConflict,
            "The filesystem no longer matches a safely resumable move state.");
    }

    private MutationResult<LifecycleMutationReceipt>? TryReconcileDelete(
        PersistentMutationReceipt receipt,
        RecoveryRecord recovery)
    {
        if (string.IsNullOrWhiteSpace(receipt.SourceRelativePath))
        {
            return MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.MutationStateConflict,
                "The delete receipt is missing its source path.");
        }

        var source = pathPolicy.ValidateExisting(
            receipt.WorkspaceId,
            receipt.SourceRelativePath,
            WorkspaceOperation.Delete,
            allowWorkspaceRoot: false);

        var sourceMissing = !source.Allowed &&
                            source.Error == WorkspaceAccessError.PathNotFound;
        var quarantineExists = File.Exists(recovery.QuarantinePath);

        if (sourceMissing && quarantineExists)
        {
            var hash = TryComputeFileHash(recovery.QuarantinePath);
            if (!hash.Success ||
                !string.Equals(hash.Value, recovery.Sha256, StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "Quarantined content could not be reconciled with recovery metadata.");
            }

            if (recovery.Status != RecoveryStatus.Quarantined)
            {
                recoveryStore.UpdateStatus(
                    recovery.Id,
                    RecoveryStatus.Quarantined);
            }

            return Complete(receipt, reconciled: true);
        }

        if (source.Allowed &&
            source.EntryKind == FileSystemEntryKind.RegularFile &&
            !quarantineExists)
        {
            return null;
        }

        return MutationResult.Fail<LifecycleMutationReceipt>(
            FileMutationError.MutationStateConflict,
            "The filesystem no longer matches a safely resumable delete state.");
    }

    private MutationResult<LifecycleMutationReceipt>? TryReconcileRestore(
        PersistentMutationReceipt receipt,
        RecoveryRecord recovery)
    {
        var destination = pathPolicy.ValidateExisting(
            recovery.WorkspaceId,
            recovery.OriginalRelativePath,
            WorkspaceOperation.Restore,
            allowWorkspaceRoot: false);

        var quarantineExists = File.Exists(recovery.QuarantinePath);

        if (!quarantineExists &&
            destination.Allowed &&
            destination.EntryKind == FileSystemEntryKind.RegularFile &&
            destination.FullPath is not null)
        {
            var hash = TryComputeFileHash(destination.FullPath);
            if (!hash.Success ||
                !string.Equals(hash.Value, recovery.Sha256, StringComparison.Ordinal))
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    "Restored content could not be reconciled with recovery metadata.");
            }

            if (recovery.Status != RecoveryStatus.Restored)
            {
                recoveryStore.UpdateStatus(
                    recovery.Id,
                    RecoveryStatus.Restored);
            }

            return Complete(receipt, reconciled: true);
        }

        if (quarantineExists &&
            !destination.Allowed &&
            destination.Error == WorkspaceAccessError.PathNotFound)
        {
            return null;
        }

        return MutationResult.Fail<LifecycleMutationReceipt>(
            FileMutationError.MutationStateConflict,
            "The filesystem no longer matches a safely resumable restore state.");
    }

    private MutationResult<LifecycleMutationReceipt> EnsureDirectoryChain(
        string workspaceId,
        string normalizedRelativePath)
    {
        var segments = normalizedRelativePath
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var currentRelativePath = string.Empty;

        foreach (var segment in segments)
        {
            currentRelativePath = currentRelativePath.Length == 0
                ? segment
                : Path.Combine(currentRelativePath, segment);

            var existing = pathPolicy.ValidateExisting(
                workspaceId,
                currentRelativePath,
                WorkspaceOperation.Create,
                allowWorkspaceRoot: false);

            if (existing.Allowed)
            {
                if (existing.EntryKind != FileSystemEntryKind.Directory)
                {
                    return MutationResult.Fail<LifecycleMutationReceipt>(
                        FileMutationError.NotADirectory,
                        $"A directory component is occupied by another entry: {currentRelativePath}");
                }

                continue;
            }

            if (existing.Error != WorkspaceAccessError.PathNotFound)
            {
                return FromAccessFailure<LifecycleMutationReceipt>(existing);
            }

            var create = pathPolicy.ValidateCreate(
                workspaceId,
                currentRelativePath,
                WorkspaceOperation.Create);

            if (!create.Allowed || create.FullPath is null)
            {
                return FromAccessFailure<LifecycleMutationReceipt>(create);
            }

            try
            {
                Directory.CreateDirectory(create.FullPath);
            }
            catch (IOException exception)
            {
                return IoFailure<LifecycleMutationReceipt>(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure<LifecycleMutationReceipt>(exception);
            }

            var verified = pathPolicy.ValidateExisting(
                workspaceId,
                currentRelativePath,
                WorkspaceOperation.Create,
                allowWorkspaceRoot: false);

            if (!verified.Allowed || verified.EntryKind != FileSystemEntryKind.Directory)
            {
                return MutationResult.Fail<LifecycleMutationReceipt>(
                    FileMutationError.MutationStateConflict,
                    $"Created directory could not be verified safely: {currentRelativePath}");
            }
        }

        return MutationResult.Ok(
            new LifecycleMutationReceipt(
                MutationId: string.Empty,
                Operation: CreateDirectoryOperation,
                WorkspaceId: workspaceId,
                SourceRelativePath: normalizedRelativePath,
                DestinationRelativePath: normalizedRelativePath,
                RecoveryId: null,
                Sha256: null,
                Replayed: false,
                Reconciled: false));
    }

    private static MutationResult<LifecycleMutationReceipt>? ValidateExistingReceipt(
        PersistentMutationReceipt? existing,
        string mutationId,
        string fingerprint)
    {
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(
                existing.Fingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.MutationIdConflict,
                $"mutationId '{mutationId}' was already used with different arguments.");
        }

        if (existing.Status == PersistentMutationStatus.Completed)
        {
            return MutationResult.Ok(ToPublicReceipt(
                existing,
                replayed: true,
                reconciled: false));
        }

        return null;
    }

    private MutationResult<LifecycleMutationReceipt> Complete(
        PersistentMutationReceipt receipt,
        bool reconciled)
    {
        var completed = receipt with
        {
            Status = PersistentMutationStatus.Completed,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        receiptStore.Update(completed);

        return MutationResult.Ok(ToPublicReceipt(
            completed,
            replayed: false,
            reconciled));
    }

    private static PersistentMutationReceipt NewStartedReceipt(
        string mutationId,
        string operation,
        string fingerprint,
        string workspaceId,
        string? sourceRelativePath,
        string? destinationRelativePath,
        string? recoveryId,
        string? sha256) =>
        new(
            mutationId,
            operation,
            fingerprint,
            workspaceId,
            sourceRelativePath,
            destinationRelativePath,
            recoveryId,
            sha256,
            PersistentMutationStatus.Started,
            DateTimeOffset.UtcNow);

    private static LifecycleMutationReceipt ToPublicReceipt(
        PersistentMutationReceipt receipt,
        bool replayed,
        bool reconciled) =>
        new(
            receipt.MutationId,
            receipt.Operation,
            receipt.WorkspaceId,
            receipt.SourceRelativePath,
            receipt.DestinationRelativePath,
            receipt.RecoveryId,
            receipt.Sha256,
            replayed,
            reconciled);

    private string CreateFingerprint(params string?[] values)
    {
        var canonical = string.Join(
            "\n",
            values.Select(value => value?.Trim() ?? "<null>"));

        return fileHasher.Compute(Encoding.UTF8.GetBytes(canonical));
    }

    private static MutationResult<LifecycleMutationReceipt>? ValidateMutationId(
        string mutationId)
    {
        var valid = !string.IsNullOrWhiteSpace(mutationId) &&
                    mutationId.Length is >= 8 and <= 128 &&
                    mutationId.All(character =>
                        char.IsLetterOrDigit(character) ||
                        character is '-' or '_' or '.');

        return valid
            ? null
            : MutationResult.Fail<LifecycleMutationReceipt>(
                FileMutationError.InvalidRequest,
                "mutationId must be 8-128 characters using only letters, digits, '.', '_' or '-'.");
    }

    private MutationResult<string> TryComputeFileHash(string path)
    {
        try
        {
            return MutationResult.Ok(fileHasher.ComputeFile(path));
        }
        catch (IOException exception)
        {
            return MutationResult.Fail<string>(
                FileMutationError.IoError,
                $"Unable to hash file: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return MutationResult.Fail<string>(
                FileMutationError.AccessDenied,
                $"Unable to hash file: {exception.Message}");
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

    private static MutationResult<T> FromMoveAccessFailure<T>(MovePathPolicyResult result)
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

    private static MutationResult<T> IoFailure<T>(Exception exception)
        where T : class =>
        MutationResult.Fail<T>(
            FileMutationError.IoError,
            $"Filesystem lifecycle operation failed: {exception.Message}");
}

internal static class MutationResultConversionExtensions
{
    public static MutationResult<TTarget> ConvertFailure<TTarget>(
        this MutationResult<string> failure)
        where TTarget : class =>
        MutationResult.Fail<TTarget>(
            failure.Error,
            failure.Message,
            failure.AccessError);
}
