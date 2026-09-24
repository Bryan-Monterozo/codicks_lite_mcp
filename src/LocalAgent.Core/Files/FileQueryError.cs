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
    IoError
}
