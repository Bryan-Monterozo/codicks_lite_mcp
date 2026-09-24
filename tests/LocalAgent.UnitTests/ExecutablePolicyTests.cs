using LocalAgent.Core.Configuration;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Execution;
using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class ExecutablePolicyTests
{
    [Fact]
    public void Evaluate_AllowsConfiguredCommand_AndReturnsEffectiveLimits()
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest(
                "demo",
                "DOTNET",
                ["TEST"],
                TimeoutSeconds: 120,
                MaxOutputBytes: 4096));

        Assert.True(result.Allowed);
        Assert.Equal(ProcessExecutionError.None, result.Error);
        Assert.Equal(120, result.EffectiveTimeoutSeconds);
        Assert.Equal(4096, result.EffectiveMaxOutputBytes);
    }


    [Fact]
    public void Evaluate_DeniesWhenExecutionIsGloballyDisabled()
    {
        var configuration = CreateConfiguration();
        configuration.Execution.Enabled = false;
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest("demo", "dotnet", ["test"]));

        Assert.False(result.Allowed);
        Assert.Equal(ProcessExecutionError.ExecutionDisabled, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_RejectsEmptyExecutable(string executable)
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest("demo", executable, ["test"]));

        Assert.False(result.Allowed);
        Assert.Equal(ProcessExecutionError.InvalidRequest, result.Error);
    }

    [Fact]
    public void Evaluate_RejectsNonPositiveRequestedLimits()
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);
        var workspace = CreateWorkspace(WorkspaceOperation.Execute);

        var timeout = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                TimeoutSeconds: 0));

        var output = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                MaxOutputBytes: 0));

        Assert.Equal(ProcessExecutionError.TimeoutOutOfRange, timeout.Error);
        Assert.Equal(ProcessExecutionError.OutputLimitOutOfRange, output.Error);
    }

    [Fact]
    public void Evaluate_DeniesUnknownOrDisabledExecutable()
    {
        var configuration = CreateConfiguration();
        configuration.Execution.Executables["disabled"] = new()
        {
            Enabled = false,
            AllowAnyArguments = true
        };
        var policy = CreatePolicy(configuration);
        var workspace = CreateWorkspace(WorkspaceOperation.Execute);

        var unknown = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest("demo", "node", ["test"]));

        var disabled = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest("demo", "disabled", ["anything"]));

        Assert.Equal(ProcessExecutionError.ExecutableNotAllowed, unknown.Error);
        Assert.Equal(ProcessExecutionError.ExecutableNotAllowed, disabled.Error);
    }

    [Fact]
    public void Evaluate_RequiresWorkspaceExecutePermission()
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Read),
            new ProcessExecutionRequest("demo", "dotnet", ["test"]));

        Assert.False(result.Allowed);
        Assert.Equal(
            ProcessExecutionError.WorkspaceExecutionDenied,
            result.Error);
    }

    [Fact]
    public void Evaluate_DeniedCommandWinsOverAllowedCommand()
    {
        var configuration = CreateConfiguration();
        configuration.Execution.Executables["git"] = new()
        {
            Enabled = true,
            AllowedCommands = ["status", "reset"],
            DeniedCommands = ["reset"]
        };
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest("demo", "git", ["reset"]));

        Assert.False(result.Allowed);
        Assert.Equal(ProcessExecutionError.CommandNotAllowed, result.Error);
    }

    [Fact]
    public void Evaluate_AllowAnyArgumentsAcceptsArbitraryFirstArgument()
    {
        var configuration = CreateConfiguration();
        configuration.Execution.Executables["flutter"] = new()
        {
            Enabled = true,
            AllowAnyArguments = true
        };
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest(
                "demo",
                "flutter",
                ["custom-command", "--flag"]));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Evaluate_RestrictedExecutableRejectsUnlistedCommand()
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest("demo", "dotnet", ["publish"]));

        Assert.False(result.Allowed);
        Assert.Equal(ProcessExecutionError.CommandNotAllowed, result.Error);
    }

    [Fact]
    public void Evaluate_EnforcesPerExecutableTimeoutOverride()
    {
        var configuration = CreateConfiguration();
        configuration.Execution.Executables["dotnet"].MaxTimeoutSeconds = 90;
        var policy = CreatePolicy(configuration);
        var workspace = CreateWorkspace(WorkspaceOperation.Execute);

        var allowed = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                TimeoutSeconds: 90));

        var denied = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                TimeoutSeconds: 91));

        Assert.True(allowed.Allowed);
        Assert.Equal(90, allowed.EffectiveTimeoutSeconds);
        Assert.Equal(ProcessExecutionError.TimeoutOutOfRange, denied.Error);
    }

    [Fact]
    public void Evaluate_EnforcesGlobalTimeoutAndOutputBounds()
    {
        var configuration = CreateConfiguration();
        configuration.Execution.MaxTimeoutSeconds = 60;
        configuration.Execution.MaxOutputBytes = 1024;
        var policy = CreatePolicy(configuration);
        var workspace = CreateWorkspace(WorkspaceOperation.Execute);

        var timeout = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                TimeoutSeconds: 61));

        var output = policy.Evaluate(
            workspace,
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                MaxOutputBytes: 1025));

        Assert.Equal(ProcessExecutionError.TimeoutOutOfRange, timeout.Error);
        Assert.Equal(ProcessExecutionError.OutputLimitOutOfRange, output.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("src")]
    [InlineData("tests/LocalAgent.UnitTests")]
    public void Evaluate_AllowsWorkspaceRelativeWorkingDirectories(
        string relativeWorkingDirectory)
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                RelativeWorkingDirectory: relativeWorkingDirectory));

        Assert.True(result.Allowed);
    }

    [Theory]
    [InlineData("/tmp")]
    [InlineData("../outside")]
    [InlineData("src/../../outside")]
    [InlineData("src\\..\\outside")]
    public void Evaluate_RejectsAbsoluteOrTraversalWorkingDirectories(
        string relativeWorkingDirectory)
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                RelativeWorkingDirectory: relativeWorkingDirectory));

        Assert.False(result.Allowed);
        Assert.Equal(
            ProcessExecutionError.InvalidWorkingDirectory,
            result.Error);
    }

    [Fact]
    public void Evaluate_RejectsUnsupportedSandboxMode()
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                ExecutionMode: ExecutionMode.Sandbox));

        Assert.Equal(
            ProcessExecutionError.UnsupportedExecutionMode,
            result.Error);
    }

    [Fact]
    public void Evaluate_RejectsExecutablePaths()
    {
        var configuration = CreateConfiguration();
        var policy = CreatePolicy(configuration);

        var result = policy.Evaluate(
            CreateWorkspace(WorkspaceOperation.Execute),
            new ProcessExecutionRequest(
                "demo",
                "/usr/bin/dotnet",
                ["test"]));

        Assert.Equal(ProcessExecutionError.ExecutableNotAllowed, result.Error);
    }

    private static AgentConfiguration CreateConfiguration()
    {
        var configuration = new AgentConfiguration
        {
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

    private static ExecutablePolicy CreatePolicy(
        AgentConfiguration configuration) =>
        new(
            configuration,
            new WorkspacePermissionEvaluator(configuration));

    private static WorkspaceDescriptor CreateWorkspace(
        params WorkspaceOperation[] operations) =>
        new(
            "demo",
            "/tmp/demo",
            true,
            operations);
}
