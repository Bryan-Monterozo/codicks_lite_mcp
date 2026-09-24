namespace LocalAgent.Core.Files;

public interface IAtomicFileWriter
{
    void CreateNew(string destinationPath, byte[] content);

    void ReplaceExisting(
        string destinationPath,
        byte[] content,
        UnixFileMode? unixFileMode);
}
