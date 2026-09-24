namespace LocalAgent.Core.Files;

public interface IFileHasher
{
    string Compute(byte[] bytes);

    string ComputeFile(string path);
}
