using LocalAgent.Core.Paths;

namespace LocalAgent.Infrastructure.Paths;

public sealed class UserPathResolver : IUserPathResolver
{
    public string Resolve(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var expanded = ExpandHome(configuredPath.Trim());
        return Path.GetFullPath(expanded);
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var homePrefix = "~" + Path.DirectorySeparatorChar;
        if (path.StartsWith(homePrefix, StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[homePrefix.Length..]);
        }

        return path;
    }
}
