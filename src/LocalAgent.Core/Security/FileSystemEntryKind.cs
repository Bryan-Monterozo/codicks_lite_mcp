namespace LocalAgent.Core.Security;

public enum FileSystemEntryKind
{
    Missing,
    RegularFile,
    Directory,
    SymbolicLink,
    Other,
    Unknown
}
