using LocalAgent.Core.Configuration;
using LocalAgent.Infrastructure.Paths;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class AgentConfigurationValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk02-validator-{Guid.NewGuid():N}");

    [Fact]
    public void Validate_AcceptsValidWorkspaceConfiguration()
    {
        var workspaceRoot = Path.Combine(_root, "workspace");
        var stateRoot = Path.Combine(_root, "state");
        var configFile = Path.Combine(_root, "config", "agent.json");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);

        var configuration = CreateValidConfiguration(workspaceRoot, stateRoot);
        var validator = new AgentConfigurationValidator(new UserPathResolver());

        var errors = validator.Validate(configuration, configFile);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsUnknownWorkspaceOperation()
    {
        var workspaceRoot = Path.Combine(_root, "workspace");
        var stateRoot = Path.Combine(_root, "state");
        var configFile = Path.Combine(_root, "config", "agent.json");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);

        var configuration = CreateValidConfiguration(workspaceRoot, stateRoot);
        configuration.Agent.Workspaces["demo"].AllowedOperations = ["read", "launch-nuclear-submarine"];

        var validator = new AgentConfigurationValidator(new UserPathResolver());
        var errors = validator.Validate(configuration, configFile);

        Assert.Contains(errors, error => error.Contains("unknown operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsStateDirectoryInsideEnabledWorkspace()
    {
        var workspaceRoot = Path.Combine(_root, "workspace");
        var stateRoot = Path.Combine(workspaceRoot, ".codicks-lite-state");
        var configFile = Path.Combine(_root, "config", "agent.json");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);

        var configuration = CreateValidConfiguration(workspaceRoot, stateRoot);
        var validator = new AgentConfigurationValidator(new UserPathResolver());

        var errors = validator.Validate(configuration, configFile);

        Assert.Contains(errors, error => error.Contains("StateDirectory", StringComparison.Ordinal));
    }

    private static AgentConfiguration CreateValidConfiguration(string workspaceRoot, string stateRoot)
    {
        return new AgentConfiguration
        {
            SchemaVersion = 1,
            Agent = new AgentOptions
            {
                StateDirectory = stateRoot,
                Workspaces = new Dictionary<string, WorkspaceOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["demo"] = new()
                    {
                        Root = workspaceRoot,
                        Enabled = true,
                        AllowedOperations = ["read", "create", "update", "move", "delete", "restore"]
                    }
                }
            }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
