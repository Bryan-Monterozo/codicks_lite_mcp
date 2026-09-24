using System.Text;
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

public sealed class HostProcessExecutionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-process-exec-{Guid.NewGuid():N}");

    public HostProcessExecutionServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ExecuteAsync_DotnetVersion_ReturnsSuccessfulStdout()
    {
        var service = CreateService(
            ("dotnet", AllowAny()));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "dotnet",
                ["--version"],
                TimeoutSeconds: 30),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExit_ReturnsNormalResult()
    {
        var service = CreateService(
            ("git", AllowAny()));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "git",
                ["definitely-not-a-git-command"],
                TimeoutSeconds: 30),
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStdoutOnly()
    {
        var service = CreateService(
            ("printf", AllowAny()));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "printf",
                ["hello-process-runner"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "hello-process-runner",
            result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStderrOnly()
    {
        var service = CreateService(
            ("ls", AllowAny()));

        var missingPath =
            $"/definitely-missing-{Guid.NewGuid():N}";

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "ls",
                [missingPath]),
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStdoutAndStderrConcurrently()
    {
        var service = CreateService(
            ("find", AllowAny()));

        var missingPath =
            $"/definitely-missing-{Guid.NewGuid():N}";

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "find",
                [".", missingPath]),
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(".", result.StandardOutput, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesLargeOutput_AndContinuesToExit()
    {
        var service = CreateService(
            ("printf", AllowAny()));

        var output = new string('x', 8_192);

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "printf",
                [output],
                MaxOutputBytes: 64),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StandardOutputTruncated);
        Assert.False(result.StandardErrorTruncated);
        Assert.True(
            Encoding.UTF8.GetByteCount(
                result.StandardOutput) <= 64);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_KillsProcessTreeAndReturnsFlag()
    {
        var service = CreateService(
            ("sleep", AllowAny()));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "sleep",
                ["5"],
                TimeoutSeconds: 1),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.True(result.DurationMilliseconds < 5_000);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_KillsProcessTreeAndReturnsFlag()
    {
        var service = CreateService(
            ("sleep", AllowAny()));

        using var cancellation =
            new CancellationTokenSource(
                TimeSpan.FromMilliseconds(100));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "sleep",
                ["5"],
                TimeoutSeconds: 10),
            cancellation.Token);

        Assert.False(result.TimedOut);
        Assert.True(result.Cancelled);
        Assert.True(result.DurationMilliseconds < 5_000);
    }

    [Fact]
    public async Task ExecuteAsync_UsesValidatedNestedWorkingDirectory()
    {
        var nested = Path.Combine(
            _root,
            "src",
            "nested");

        Directory.CreateDirectory(nested);

        var service = CreateService(
            ("pwd", AllowAny()));

        var result = await service.ExecuteAsync(
            new ProcessExecutionRequest(
                "demo",
                "pwd",
                [],
                RelativeWorkingDirectory: "src/nested"),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.EndsWith(
            Path.Combine("src", "nested"),
            result.StandardOutput.Trim(),
            StringComparison.Ordinal);
        Assert.Equal(
            Path.Combine("src", "nested"),
            result.RelativeWorkingDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_DeniedExecutable_DoesNotStartProcess()
    {
        var service = CreateService(
            ("dotnet", AllowAny()));

        var exception =
            await Assert.ThrowsAsync<ProcessExecutionException>(
                () => service.ExecuteAsync(
                    new ProcessExecutionRequest(
                        "demo",
                        "git",
                        ["status"]),
                    CancellationToken.None));

        Assert.Equal(
            ProcessExecutionError.ExecutableNotAllowed,
            exception.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ConfiguredButMissingExecutable_ReturnsStartFailure()
    {
        const string executable =
            "codicks-definitely-missing-executable";

        var service = CreateService(
            (executable, AllowAny()));

        var exception =
            await Assert.ThrowsAsync<ProcessExecutionException>(
                () => service.ExecuteAsync(
                    new ProcessExecutionRequest(
                        "demo",
                        executable,
                        ["anything"]),
                    CancellationToken.None));

        Assert.Equal(
            ProcessExecutionError.ProcessStartFailed,
            exception.Error);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("src/../../outside")]
    public async Task ExecuteAsync_TraversalWorkingDirectory_IsRejected(
        string workingDirectory)
    {
        var service = CreateService(
            ("dotnet", AllowAny()));

        var exception =
            await Assert.ThrowsAsync<ProcessExecutionException>(
                () => service.ExecuteAsync(
                    new ProcessExecutionRequest(
                        "demo",
                        "dotnet",
                        ["--version"],
                        RelativeWorkingDirectory: workingDirectory),
                    CancellationToken.None));

        Assert.Equal(
            ProcessExecutionError.InvalidWorkingDirectory,
            exception.Error);
    }

    [Fact]
    public async Task ExecuteAsync_MissingWorkingDirectory_IsRejected()
    {
        var service = CreateService(
            ("dotnet", AllowAny()));

        var exception =
            await Assert.ThrowsAsync<ProcessExecutionException>(
                () => service.ExecuteAsync(
                    new ProcessExecutionRequest(
                        "demo",
                        "dotnet",
                        ["--version"],
                        RelativeWorkingDirectory: "missing-directory"),
                    CancellationToken.None));

        Assert.Equal(
            ProcessExecutionError.InvalidWorkingDirectory,
            exception.Error);
    }


    [Fact]
    public async Task ExecuteAsync_SymlinkWorkingDirectory_IsRejected()
    {
        var outside = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-process-outside-{Guid.NewGuid():N}");

        Directory.CreateDirectory(outside);

        try
        {
            var link = Path.Combine(
                _root,
                "outside-link");

            Directory.CreateSymbolicLink(
                link,
                outside);

            var service = CreateService(
                ("dotnet", AllowAny()));

            var exception =
                await Assert.ThrowsAsync<ProcessExecutionException>(
                    () => service.ExecuteAsync(
                        new ProcessExecutionRequest(
                            "demo",
                            "dotnet",
                            ["--version"],
                            RelativeWorkingDirectory: "outside-link"),
                        CancellationToken.None));

            Assert.Equal(
                ProcessExecutionError.InvalidWorkingDirectory,
                exception.Error);
        }
        finally
        {
            if (Directory.Exists(outside))
            {
                Directory.Delete(
                    outside,
                    recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_RemovesSecretLikeEnvironmentVariables()
    {
        const string variableName =
            "CODICKS_TEST_SECRET_TOKEN";

        var original =
            Environment.GetEnvironmentVariable(
                variableName);

        Environment.SetEnvironmentVariable(
            variableName,
            "must-not-reach-child");

        try
        {
            var service = CreateService(
                ("env", AllowAny()));

            var result = await service.ExecuteAsync(
                new ProcessExecutionRequest(
                    "demo",
                    "env",
                    []),
                CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain(
                variableName,
                result.StandardOutput,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "must-not-reach-child",
                result.StandardOutput,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                variableName,
                original);
        }
    }

    private HostProcessExecutionService CreateService(
        params (string Executable, ExecutableExecutionOptions Options)[] executables)
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
                    MaxOutputBytes = 65_536
                }
            };

        foreach (var executable in executables)
        {
            configuration.Execution.Executables[
                executable.Executable] =
                executable.Options;
        }

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

        return new HostProcessExecutionService(
            workspaceResolver,
            pathPolicy,
            executablePolicy);
    }

    private static ExecutableExecutionOptions AllowAny() =>
        new()
        {
            Enabled = true,
            AllowAnyArguments = true
        };

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
