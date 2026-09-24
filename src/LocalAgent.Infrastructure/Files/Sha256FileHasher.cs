using System.Security.Cryptography;
using LocalAgent.Core.Files;

namespace LocalAgent.Infrastructure.Files;

public sealed class Sha256FileHasher : IFileHasher
{
    public string Compute(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public string ComputeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.SequentialScan);

        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
