using LocalAgent.Core.Configuration;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspaceTopologyValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk03-topology-{Guid.NewGuid():N}");

    [Fact]
    public void Validate_RejectsOverlappingEnabledWorkspaceRoots()
    {
        var parent = Path.Combine(_root, "parent");
        var child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child);

        var configuration = CreateConfiguration(
            ("parent", parent),
            ("child", child));

        var validator = new WorkspaceTopologyValidator(new UserPathResolver());
        var errors = validator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsSymbolicLinkWorkspaceRoot()
    {
        var realRoot = Path.Combine(_root, "real");
        var linkedRoot = Path.Combine(_root, "linked");
        Directory.CreateDirectory(realRoot);
        Directory.CreateDirectory(_root);
        Directory.CreateSymbolicLink(linkedRoot, realRoot);

        var configuration = CreateConfiguration(("linked", linkedRoot));

        var validator = new WorkspaceTopologyValidator(new UserPathResolver());
        var errors = validator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("symbolic link", StringComparison.OrdinalIgnoreCase));
    }

    private static AgentConfiguration CreateConfiguration(params (string Id, string Root)[] workspaces)
    {
        var configuration = new AgentConfiguration();

        foreach (var workspace in workspaces)
        {
            configuration.Agent.Workspaces[workspace.Id] = new WorkspaceOptions
            {
                Root = workspace.Root,
                Enabled = true,
                AllowedOperations = ["read"]
            };
        }

        return configuration;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
