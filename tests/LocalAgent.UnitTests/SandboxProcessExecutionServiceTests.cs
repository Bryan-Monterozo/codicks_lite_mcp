using LocalAgent.Core.Configuration;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Execution;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class SandboxProcessExecutionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-sandbox-{Guid.NewGuid():N}");

    public SandboxProcessExecutionServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ExecuteAsync_BuildsIsolatedContainerInvocation()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var capturePath = Path.Combine(
            _root,
            "container-args.txt");

        var runtimePath = CreateFakeRuntime(
            capturePath);

        var nested = Path.Combine(
            _root,
            "src");

        Directory.CreateDirectory(nested);

        var service = CreateService(
            runtimePath);

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test", "Example.slnx"],
                RelativeWorkingDirectory: "src",
                ExecutionMode: ExecutionMode.Sandbox),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("sandbox-ok", result.StandardOutput);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);

        var arguments =
            File.ReadAllLines(capturePath);

        Assert.Contains("run", arguments);
        Assert.Contains("--rm", arguments);
        Assert.Contains("--read-only", arguments);
        Assert.Contains("--network", arguments);
        Assert.Contains("none", arguments);
        Assert.Contains("--no-dns", arguments);
        Assert.Contains("--cpus", arguments);
        Assert.Contains("2", arguments);
        Assert.Contains("--memory", arguments);
        Assert.Contains("2048M", arguments);
        Assert.Contains("--tmpfs", arguments);
        Assert.Contains("/tmp:size=256M,mode=1777", arguments);
        Assert.Contains("--volume", arguments);
        Assert.Contains(
            $"{_root}:/workspace",
            arguments);
        Assert.Contains("--workdir", arguments);
        Assert.Contains("/workspace/src", arguments);
        Assert.Contains("HOME=/tmp", arguments);
        Assert.Contains(
            "DOTNET_CLI_HOME=/tmp/dotnet-home",
            arguments);
        Assert.Contains(
            "mcr.microsoft.com/dotnet/sdk:10.0",
            arguments);
        Assert.Contains("dotnet", arguments);
        Assert.Contains("test", arguments);
        Assert.Contains("Example.slnx", arguments);

        var home =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        Assert.DoesNotContain(
            arguments,
            argument =>
                !string.IsNullOrWhiteSpace(home) &&
                argument.Contains(
                    home,
                    StringComparison.Ordinal) &&
                !argument.Contains(
                    _root,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_NetworkCanBeExplicitlyEnabled()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var capturePath = Path.Combine(
            _root,
            "network-args.txt");

        var runtimePath = CreateFakeRuntime(
            capturePath);

        var service = CreateService(
            runtimePath,
            sandbox =>
                sandbox.NetworkEnabled = true);

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                ExecutionMode: ExecutionMode.Sandbox),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);

        var arguments =
            File.ReadAllLines(capturePath);

        Assert.DoesNotContain(
            "--network",
            arguments);

        Assert.DoesNotContain(
            "--no-dns",
            arguments);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_PerformsForcedCleanup()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var capturePath = Path.Combine(
            _root,
            "timeout-args.txt");

        var runtimePath = CreateFakeRuntime(
            capturePath);

        var service = CreateService(
            runtimePath);

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "sandbox-timeout",
                [],
                TimeoutSeconds: 1,
                ExecutionMode: ExecutionMode.Sandbox),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);

        var capture =
            await File.ReadAllTextAsync(
                capturePath);

        Assert.Contains(
            "delete",
            capture,
            StringComparison.Ordinal);

        Assert.Contains(
            "--force",
            capture,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_PerformsForcedCleanup()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var capturePath = Path.Combine(
            _root,
            "cancel-args.txt");

        var runtimePath = CreateFakeRuntime(
            capturePath);

        var service = CreateService(
            runtimePath);

        using var cancellation =
            new CancellationTokenSource(
                TimeSpan.FromMilliseconds(100));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "sandbox-timeout",
                [],
                TimeoutSeconds: 10,
                ExecutionMode: ExecutionMode.Sandbox),
            cancellation.Token);

        Assert.False(result.TimedOut);
        Assert.True(result.Cancelled);

        var capture =
            await File.ReadAllTextAsync(
                capturePath);

        Assert.Contains(
            "delete",
            capture,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_MissingBackend_IsReportedClearly()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var service = CreateService(
            Path.Combine(
                _root,
                "missing-container-runtime"));

        var exception =
            await Assert.ThrowsAsync<ProcessExecutionException>(
                () => service.ExecuteAsync(
                    new ProcessExecutionRequest(
                        "demo",
                        "dotnet",
                        ["test"],
                        ExecutionMode: ExecutionMode.Sandbox),
                    CancellationToken.None));

        Assert.Equal(
            ProcessExecutionError.SandboxUnavailable,
            exception.Error);
    }

    [Fact]
    public async Task HostAndSandboxModes_RemainDistinct()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var capturePath = Path.Combine(
            _root,
            "router-args.txt");

        var runtimePath = CreateFakeRuntime(
            capturePath);

        var dependencies =
            CreateDependencies(
                runtimePath);

        var router =
            new ProcessExecutionRouter(
                new HostProcessExecutionService(
                    dependencies.WorkspaceResolver,
                    dependencies.PathPolicy,
                    dependencies.ExecutablePolicy),
                new SandboxProcessExecutionService(
                    dependencies.Configuration,
                    dependencies.WorkspaceResolver,
                    dependencies.PathPolicy,
                    dependencies.ExecutablePolicy));

        var host = await router.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["--version"],
                ExecutionMode: ExecutionMode.Host),
            CancellationToken.None);

        var sandbox = await router.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["test"],
                ExecutionMode: ExecutionMode.Sandbox),
            CancellationToken.None);

        Assert.Equal(0, host.ExitCode);
        Assert.NotEqual(
            "sandbox-ok",
            host.StandardOutput.Trim());

        Assert.Equal(0, sandbox.ExitCode);
        Assert.Equal(
            "sandbox-ok",
            sandbox.StandardOutput);
    }

    private SandboxProcessExecutionService CreateService(
        string runtimePath,
        Action<SandboxExecutionOptions>? configure = null)
    {
        var dependencies =
            CreateDependencies(
                runtimePath,
                configure);

        return new SandboxProcessExecutionService(
            dependencies.Configuration,
            dependencies.WorkspaceResolver,
            dependencies.PathPolicy,
            dependencies.ExecutablePolicy);
    }

    private SandboxDependencies CreateDependencies(
        string runtimePath,
        Action<SandboxExecutionOptions>? configure = null)
    {
        var configuration =
            new AgentConfiguration
            {
                Agent = new AgentOptions
                {
                    StateDirectory = Path.Combine(
                        _root,
                        ".state"),
                    Workspaces =
                        new Dictionary<string, WorkspaceOptions>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            ["demo"] = new()
                            {
                                Root = _root,
                                Enabled = true,
                                AllowedOperations =
                                    ["read", "execute"]
                            }
                        }
                },
                Execution = new ExecutionOptions
                {
                    Enabled = true,
                    MaxTimeoutSeconds = 30,
                    MaxOutputBytes = 65_536,
                    Sandbox = new SandboxExecutionOptions
                    {
                        Enabled = true,
                        RuntimeExecutable = runtimePath,
                        Image =
                            "mcr.microsoft.com/dotnet/sdk:10.0",
                        CpuCount = 2,
                        MemoryMegabytes = 2_048,
                        TmpfsMegabytes = 256,
                        ReadOnlyRoot = true,
                        NetworkEnabled = false
                    }
                }
            };

        configuration.Execution.Executables[
            "dotnet"] =
            new ExecutableExecutionOptions
            {
                Enabled = true,
                AllowAnyArguments = true
            };

        configuration.Execution.Executables[
            "sandbox-timeout"] =
            new ExecutableExecutionOptions
            {
                Enabled = true,
                AllowAnyArguments = true
            };

        configure?.Invoke(
            configuration.Execution.Sandbox);

        IUserPathResolver pathResolver =
            new UserPathResolver();

        IWorkspaceRegistry workspaceRegistry =
            new WorkspaceRegistry(
                configuration,
                pathResolver);

        IWorkspaceResolver workspaceResolver =
            new WorkspaceResolver(
                workspaceRegistry);

        IWorkspacePermissionEvaluator permissionEvaluator =
            new WorkspacePermissionEvaluator(
                configuration);

        IDenyPathMatcher denyPathMatcher =
            new DenyPathMatcher(
                AgentSecurityDefaults.GetEffectiveDenyGlobs(
                    configuration.Agent.Security.DenyGlobs));

        IFileSystemEntryInspector entryInspector =
            new MacOsFileSystemEntryInspector();

        IWorkspacePathPolicy pathPolicy =
            new WorkspacePathPolicy(
                workspaceResolver,
                permissionEvaluator,
                denyPathMatcher,
                entryInspector);

        IExecutablePolicy executablePolicy =
            new ExecutablePolicy(
                configuration,
                permissionEvaluator);

        return new SandboxDependencies(
            configuration,
            workspaceResolver,
            pathPolicy,
            executablePolicy);
    }

    private string CreateFakeRuntime(
        string capturePath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }

        var runtimePath = Path.Combine(
            _root,
            $"fake-container-{Guid.NewGuid():N}");

        var escapedCapturePath =
            capturePath.Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);

        var script = string.Join(
            '\n',
            "#!/bin/sh",
            $"capture=\"{escapedCapturePath}\"",
            "printf '%s\\n' \"$@\" >> \"$capture\"",
            "",
            "if [ \"$1\" = \"delete\" ]; then",
            "  exit 0",
            "fi",
            "",
            "case \" $* \" in",
            "  *\" sandbox-timeout \"*)",
            "    sleep 5",
            "    ;;",
            "esac",
            "",
            "printf 'sandbox-ok'",
            "");

        File.WriteAllText(
            runtimePath,
            script);

        File.SetUnixFileMode(
            runtimePath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        return runtimePath;
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

    private sealed record SandboxDependencies(
        AgentConfiguration Configuration,
        IWorkspaceResolver WorkspaceResolver,
        IWorkspacePathPolicy PathPolicy,
        IExecutablePolicy ExecutablePolicy);
}
