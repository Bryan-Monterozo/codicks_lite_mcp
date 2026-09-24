using LocalAgent.Core.Configuration;
using LocalAgent.Infrastructure.Paths;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class ExecutionConfigurationValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-execution-validator-{Guid.NewGuid():N}");

    [Fact]
    public void Validate_AcceptsExecutionConfigurationAndExecuteWorkspacePermission()
    {
        var configuration = CreateValidConfiguration();
        var errors = Validate(configuration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsInvalidGlobalExecutionLimits()
    {
        var configuration = CreateValidConfiguration();
        configuration.Execution.MaxTimeoutSeconds = 0;
        configuration.Execution.MaxOutputBytes = 0;

        var errors = Validate(configuration);

        Assert.Contains(
            errors,
            error => error.Contains(
                "Execution.MaxTimeoutSeconds",
                StringComparison.Ordinal));

        Assert.Contains(
            errors,
            error => error.Contains(
                "Execution.MaxOutputBytes",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsExecutableTimeoutAboveGlobalMaximum()
    {
        var configuration = CreateValidConfiguration();
        configuration.Execution.Executables["dotnet"].MaxTimeoutSeconds = 601;

        var errors = Validate(configuration);

        Assert.Contains(
            errors,
            error => error.Contains(
                "cannot exceed Execution.MaxTimeoutSeconds",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsEnabledRestrictedExecutableWithoutAllowedCommands()
    {
        var configuration = CreateValidConfiguration();
        configuration.Execution.Executables["dotnet"].AllowedCommands = [];

        var errors = Validate(configuration);

        Assert.Contains(
            errors,
            error => error.Contains(
                "must allow at least one command",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsExecutablePathAndDuplicateCommand()
    {
        var configuration = CreateValidConfiguration();
        configuration.Execution.Executables["/usr/bin/tool"] = new()
        {
            Enabled = true,
            AllowedCommands = ["run", "RUN"]
        };

        var errors = Validate(configuration);

        Assert.Contains(
            errors,
            error => error.Contains(
                "must be a command name",
                StringComparison.Ordinal));

        Assert.Contains(
            errors,
            error => error.Contains(
                "duplicate command",
                StringComparison.Ordinal));
    }

    private IReadOnlyList<string> Validate(
        AgentConfiguration configuration)
    {
        var configFile = Path.Combine(
            _root,
            "config",
            "agent.json");

        Directory.CreateDirectory(
            Path.GetDirectoryName(configFile)!);

        var validator =
            new AgentConfigurationValidator(
                new UserPathResolver());

        return validator.Validate(
            configuration,
            configFile);
    }

    private AgentConfiguration CreateValidConfiguration()
    {
        var workspaceRoot = Path.Combine(
            _root,
            "workspace");

        var stateRoot = Path.Combine(
            _root,
            "state");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);

        var configuration = new AgentConfiguration
        {
            Agent = new AgentOptions
            {
                StateDirectory = stateRoot,
                Workspaces =
                    new Dictionary<string, WorkspaceOptions>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["demo"] = new()
                        {
                            Root = workspaceRoot,
                            Enabled = true,
                            AllowedOperations = ["read", "execute"]
                        }
                    }
            },
            Execution = new ExecutionOptions
            {
                Enabled = true,
                MaxTimeoutSeconds = 600,
                MaxOutputBytes = 262_144
            }
        };

        configuration.Execution.Executables["dotnet"] = new()
        {
            Enabled = true,
            AllowedCommands = ["build", "test", "restore"]
        };

        return configuration;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }
}
