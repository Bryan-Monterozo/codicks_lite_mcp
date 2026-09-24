namespace LocalAgent.Core.Files;

public enum FileQueryError
{
    None,
    InvalidRequest,
    AccessDenied,
    NotFound,
    NotAFile,
    NotADirectory,
    UnsupportedTextEncoding,
    Conflict,
    FileTooLarge,
    InvalidPatch,
    PatchConflict,
    PatchTooLarge,
    IoError
}
