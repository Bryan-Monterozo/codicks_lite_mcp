namespace LocalAgent.Infrastructure.Files;

internal static class MutationValidation
{
    public static bool IsSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 64 &&
               trimmed.All(Uri.IsHexDigit);
    }
}
