using System.Security.Cryptography;
using System.Text;

namespace LocalAgent.Host.Mcp;

internal static class FileReviewAuditMetadata
{
    public static string? NormalizeSha256(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed =
            value.Trim();

        return trimmed.Length == 64 &&
               trimmed.All(Uri.IsHexDigit)
            ? trimmed.ToUpperInvariant()
            : null;
    }

    public static string? ComputeUtf8Sha256(
        string? value)
    {
        if (value is null)
        {
            return null;
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));
    }
}
