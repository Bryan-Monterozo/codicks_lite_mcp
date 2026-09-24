namespace LocalAgent.Core.Files;

public enum FileMutationError
{
    None,
    InvalidRequest,
    AccessDenied,
    AlreadyExists,
    NotFound,
    NotAFile,
    Conflict,
    FileTooLarge,
    UnsupportedTextEncoding,
    BackupFailed,
    AtomicWriteFailed,
    IoError,
    NotADirectory,
    RecoveryNotFound,
    RecoveryUnavailable,
    MutationIdConflict,
    MutationStateConflict
}
