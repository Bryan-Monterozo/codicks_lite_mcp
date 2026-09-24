using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;

namespace LocalAgent.Infrastructure.Security;

public sealed class WorkspaceTopologyValidator(IUserPathResolver pathResolver)
{
    public IReadOnlyList<string> Validate(AgentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();
        var enabledRoots = new List<ResolvedWorkspaceRoot>();

        foreach (var pair in configuration.Agent.Workspaces)
        {
            if (!pair.Value.Enabled || string.IsNullOrWhiteSpace(pair.Value.Root))
            {
                continue;
            }

            string root;
            try
            {
                root = pathResolver.Resolve(pair.Value.Root);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var directoryInfo = new DirectoryInfo(root);
                if (directoryInfo.LinkTarget is not null)
                {
                    errors.Add($"Enabled workspace '{pair.Key}' root cannot be a symbolic link: {root}");
                    continue;
                }
            }
            catch (IOException exception)
            {
                errors.Add($"Unable to inspect workspace '{pair.Key}' root '{root}': {exception.Message}");
                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                errors.Add($"Unable to inspect workspace '{pair.Key}' root '{root}': {exception.Message}");
                continue;
            }

            enabledRoots.Add(new ResolvedWorkspaceRoot(pair.Key, Path.GetFullPath(root)));
        }

        for (var leftIndex = 0; leftIndex < enabledRoots.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < enabledRoots.Count; rightIndex++)
            {
                var left = enabledRoots[leftIndex];
                var right = enabledRoots[rightIndex];

                if (IsSameOrDescendant(left.Root, right.Root) || IsSameOrDescendant(right.Root, left.Root))
                {
                    errors.Add(
                        $"Enabled workspace roots must not duplicate or overlap: " +
                        $"'{left.Id}' ({left.Root}) and '{right.Id}' ({right.Root}).");
                }
            }
        }

        return errors;
    }

    private static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.GetFullPath(rootPath);

        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ResolvedWorkspaceRoot(string Id, string Root);
}
