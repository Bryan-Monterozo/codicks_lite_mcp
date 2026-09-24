using System.Text;
using System.Text.RegularExpressions;

namespace LocalAgent.Infrastructure.Files;

internal sealed class SearchExcludeMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private readonly List<Regex> _patterns;

    public SearchExcludeMatcher(IEnumerable<string> globs)
    {
        ArgumentNullException.ThrowIfNull(globs);

        _patterns = globs
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CompileGlob)
            .ToList();
    }

    public bool IsExcluded(string normalizedRelativePath)
    {
        ArgumentNullException.ThrowIfNull(normalizedRelativePath);
        var candidate = NormalizeForMatch(normalizedRelativePath);
        return _patterns.Any(pattern => pattern.IsMatch(candidate));
    }

    private static Regex CompileGlob(string glob)
    {
        var normalized = NormalizeForMatch(glob);
        var builder = new StringBuilder("^");

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];

            if (character == '*')
            {
                var isDoubleStar = index + 1 < normalized.Length && normalized[index + 1] == '*';
                if (isDoubleStar)
                {
                    var followedBySlash = index + 2 < normalized.Length && normalized[index + 2] == '/';
                    if (followedBySlash)
                    {
                        builder.Append("(?:.*/)?");
                        index += 2;
                    }
                    else
                    {
                        builder.Append(".*");
                        index++;
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }

                continue;
            }

            if (character == '?')
            {
                builder.Append("[^/]");
                continue;
            }

            if (character == '/')
            {
                builder.Append('/');
                continue;
            }

            builder.Append(Regex.Escape(character.ToString()));
        }

        builder.Append('$');

        return new Regex(
            builder.ToString(),
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            MatchTimeout);
    }

    private static string NormalizeForMatch(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }
}
