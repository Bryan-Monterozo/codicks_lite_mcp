namespace LocalAgent.Core.Security;

public interface IDenyPathMatcher
{
    bool IsDenied(string normalizedRelativePath, out string? matchedPattern);
}
