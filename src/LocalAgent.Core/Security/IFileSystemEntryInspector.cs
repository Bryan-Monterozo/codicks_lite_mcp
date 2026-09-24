namespace LocalAgent.Core.Security;

public interface IFileSystemEntryInspector
{
    FileSystemEntryInspection Inspect(string fullPath);
}
