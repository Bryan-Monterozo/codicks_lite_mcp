namespace LocalAgent.Core.Security;

public interface IFileSystemDeviceInspector
{
    string GetDeviceId(string existingPath);
}
