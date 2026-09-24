using LocalAgent.Core.Configuration;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspaceRegistryTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk02-registry-{Guid.NewGuid():N}");

    [Fact]
    public void Registry_ResolvesConfiguredWorkspaceAndOperations()
    {
        Directory.CreateDirectory(_workspaceRoot);

        var configuration = new AgentConfiguration
        {
            Agent = new AgentOptions
            {
                Workspaces = new Dictionary<string, WorkspaceOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["demo"] = new()
                    {
                        Root = _workspaceRoot,
                        Enabled = true,
                        AllowedOperations = ["read", "update"]
                    }
                }
            }
        };

        var registry = new WorkspaceRegistry(configuration, new UserPathResolver());

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet("DEMO", out var workspace));
        var resolved = Assert.IsType<WorkspaceDescriptor>(workspace);
        Assert.Equal(Path.GetFullPath(_workspaceRoot), resolved.Root);
        Assert.Contains(WorkspaceOperation.Read, resolved.AllowedOperations);
        Assert.Contains(WorkspaceOperation.Update, resolved.AllowedOperations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }
}
