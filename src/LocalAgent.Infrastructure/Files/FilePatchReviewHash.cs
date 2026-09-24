using System.Security.Cryptography;
using System.Text;

namespace LocalAgent.Infrastructure.Files;

internal static class FilePatchReviewHash
{
    public static string Compute(
        string relativePath,
        string baseSha256,
        string proposedSha256,
        string patchSha256,
        string unifiedDiff)
    {
        var payload = string.Join(
            "\n",
            relativePath,
            baseSha256,
            proposedSha256,
            patchSha256,
            unifiedDiff);

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(payload)));
    }
}
