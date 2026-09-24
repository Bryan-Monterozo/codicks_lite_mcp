using LocalAgent.Core.Files;

namespace LocalAgent.Infrastructure.Files;

public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void CreateNew(string destinationPath, byte[] content) =>
        DurableFilePersistence.CreateNew(
            destinationPath,
            content,
            unixFileMode: null);

    public void ReplaceExisting(
        string destinationPath,
        byte[] content,
        UnixFileMode? unixFileMode) =>
        DurableFilePersistence.ReplaceExisting(
            destinationPath,
            content,
            unixFileMode);
}
