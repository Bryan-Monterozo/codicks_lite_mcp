using LocalAgent.Core.Platform;
using LocalAgent.Core.Security;
using LocalAgent.Host;
using LocalAgent.Infrastructure.Platform;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class LocalCommandSurfaceIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot(
            "lcmd");

    [Fact]
    public async Task DeploymentConfigureAndApply_KeepSecretOutOfFiles_AndLeaveAgentConfigUntouched()
    {
        var applicationRoot =
            Path.Combine(
                _root,
                "app");

        var paths =
            PlatformApplicationPaths.CreateWindows(
                applicationRoot);

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                paths.DefaultConfigFilePath)!);

        const string agentConfig =
            "{\"sentinel\":\"unchanged\"}";

        await File.WriteAllTextAsync(
            paths.DefaultConfigFilePath,
            agentConfig);

        var mcpCommand =
            Path.Combine(
                _root,
                "LocalAgent.Host.exe");

        await File.WriteAllTextAsync(
            mcpCommand,
            "placeholder");

        var profileDirectory =
            Path.Combine(
                _root,
                "profiles");

        Directory.CreateDirectory(
            profileDirectory);

        var secretStore =
            new FakeSecretStore();

        var runner =
            new FakeTunnelClientRunner();

        var services =
            new ServiceCollection();

        services.AddSingleton<
            IPlatformApplicationPaths>(
            paths);

        services.AddSingleton(
            new LocalDeploymentConfigurationStore(
                paths));

        services.AddSingleton<
            IPlatformSecretStore>(
            secretStore);

        services.AddSingleton<
            ITunnelClientRunner>(
            runner);

        await using var provider =
            services.BuildServiceProvider();

        const string runtimeKey =
            "runtime-secret-value";

        var configured =
            await LocalDeploymentCommand.TryRunAsync(
                [
                    "--local-deployment",
                    "configure",
                    "--non-interactive",
                    "--tunnel-id",
                    "tunnel_test123",
                    "--profile",
                    "test-profile",
                    "--mcp-command",
                    mcpCommand,
                    "--profile-dir",
                    profileDirectory,
                    "--runtime-key",
                    runtimeKey
                ],
                provider);

        Assert.True(
            configured);

        var deploymentPath =
            Path.Combine(
                paths.ApplicationRoot,
                "config",
                "deployment.json");

        Assert.True(
            File.Exists(
                deploymentPath));

        var deploymentJson =
            await File.ReadAllTextAsync(
                deploymentPath);

        Assert.DoesNotContain(
            runtimeKey,
            deploymentJson,
            StringComparison.Ordinal);

        Assert.Equal(
            runtimeKey,
            secretStore.Read(
                "CodicksLiteMcp.OpenAI.RuntimeApiKey"));

        Assert.Equal(
            agentConfig,
            await File.ReadAllTextAsync(
                paths.DefaultConfigFilePath));

        var applied =
            await LocalDeploymentCommand.TryRunAsync(
                [
                    "--local-deployment",
                    "apply",
                    "--non-interactive",
                    "--force"
                ],
                provider);

        Assert.True(
            applied);

        var invocation =
            Assert.Single(
                runner.Invocations);

        Assert.Equal(
            runtimeKey,
            invocation.RuntimeKey);

        Assert.Contains(
            "init",
            invocation.Arguments);

        Assert.Contains(
            "env:CONTROL_PLANE_API_KEY",
            invocation.Arguments);

        Assert.DoesNotContain(
            runtimeKey,
            invocation.Arguments);

        Assert.Equal(
            agentConfig,
            await File.ReadAllTextAsync(
                paths.DefaultConfigFilePath));

        Assert.True(
            await LocalDeploymentCommand.TryRunAsync(
                [
                    "--local-deployment",
                    "doctor"
                ],
                provider));

        Assert.True(
            await LocalDeploymentCommand.TryRunAsync(
                [
                    "--local-deployment",
                    "run"
                ],
                provider));

        Assert.Equal(
            3,
            runner.Invocations.Count);

        Assert.Equal(
            "doctor",
            runner.Invocations[1].Arguments[0]);

        Assert.Equal(
            "run",
            runner.Invocations[2].Arguments[0]);

        Assert.All(
            runner.Invocations,
            invocation =>
                Assert.Equal(
                    runtimeKey,
                    invocation.RuntimeKey));
    }

    [Fact]
    public async Task SessionCommand_RoutesToLocalControlClient()
    {
        var client =
            new FakeSessionControlClient();

        var services =
            new ServiceCollection();

        services.AddSingleton<
            ILocalSessionControlClient>(
            client);

        await using var provider =
            services.BuildServiceProvider();

        var handled =
            await LocalSessionCommand.TryRunAsync(
                [
                    "--local-session",
                    "full",
                    "--otp",
                    "123456",
                    "--for",
                    "7"
                ],
                provider);

        Assert.True(
            handled);

        var request =
            Assert.Single(
                client.Requests);

        Assert.Equal(
            "unlock",
            request.Action);

        Assert.Equal(
            "Full",
            request.Mode);

        Assert.Equal(
            "123456",
            request.Otp);

        Assert.Equal(
            7,
            request.LeaseMinutes);
    }

    [Fact]
    public async Task ReleaseRollback_SwapsPointerFiles()
    {
        var paths =
            PlatformApplicationPaths.CreateWindows(
                Path.Combine(
                    _root,
                    "release-app"));

        var releasesRoot =
            Path.Combine(
                paths.ApplicationRoot,
                "releases");

        Directory.CreateDirectory(
            Path.Combine(
                releasesRoot,
                "1.2.2-release"));

        Directory.CreateDirectory(
            Path.Combine(
                releasesRoot,
                "1.2.3-release"));

        Directory.CreateDirectory(
            paths.ApplicationRoot);

        await File.WriteAllTextAsync(
            Path.Combine(
                paths.ApplicationRoot,
                "current.txt"),
            "1.2.3-release");

        await File.WriteAllTextAsync(
            Path.Combine(
                paths.ApplicationRoot,
                "previous.txt"),
            "1.2.2-release");

        var services =
            new ServiceCollection();

        services.AddSingleton<
            IPlatformApplicationPaths>(
            paths);

        await using var provider =
            services.BuildServiceProvider();

        var handled =
            await LocalInfoCommand.TryRunAsync(
                [
                    "--local-info",
                    "rollback"
                ],
                provider);

        Assert.True(
            handled);

        Assert.Equal(
            "1.2.2-release",
            (
                await File.ReadAllTextAsync(
                    Path.Combine(
                        paths.ApplicationRoot,
                        "current.txt")))
                .Trim());

        Assert.Equal(
            "1.2.3-release",
            (
                await File.ReadAllTextAsync(
                    Path.Combine(
                        paths.ApplicationRoot,
                        "previous.txt")))
                .Trim());
    }

    [Fact]
    public void WindowsPowerShellWrappers_AreThinDispatchers()
    {
        var root =
            FindRepositoryRoot();

        var main =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "scripts",
                    "codicks-lite-windows.ps1"));

        var control =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "scripts",
                    "codicks-lite-control-windows.ps1"));

        var setup =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "scripts",
                    "setup_config_windows.ps1"));

        Assert.Contains(
            "--local-session",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "--local-deployment",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "--local-backup-list",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "--local-backup-show",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "--local-backup-restore",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"releases\"",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"rollback\"",
            main,
            StringComparison.Ordinal);

        foreach (var script in
                 new[]
                 {
                     main,
                     control,
                     setup
                 })
        {
            Assert.DoesNotContain(
                "Invoke-Expression",
                script,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "cmd.exe /c",
                script,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "powershell -Command",
                script,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "pwsh -Command",
                script,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "Codicks.Lite.Mcp.slnx")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root could not be located from the test output directory.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(
                    _root))
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FakeSecretStore :
        IPlatformSecretStore
    {
        private readonly Dictionary<string, string> _values =
            new(
                StringComparer.Ordinal);

        public bool Contains(
            string serviceName) =>
            _values.ContainsKey(
                serviceName);

        public string? Read(
            string serviceName) =>
            _values.TryGetValue(
                serviceName,
                out var value)
                ? value
                : null;

        public void Store(
            string serviceName,
            string secret)
        {
            _values[serviceName] =
                secret;
        }

        public bool Delete(
            string serviceName) =>
            _values.Remove(
                serviceName);
    }

    private sealed class FakeTunnelClientRunner :
        ITunnelClientRunner
    {
        public List<Invocation> Invocations { get; } =
            [];

        public Task<int> RunAsync(
            IReadOnlyList<string> arguments,
            string runtimeApiKey,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(
                new Invocation(
                    arguments.ToArray(),
                    runtimeApiKey));

            return Task.FromResult(
                0);
        }
    }

    private sealed record Invocation(
        IReadOnlyList<string> Arguments,
        string RuntimeKey);

    private sealed class FakeSessionControlClient :
        ILocalSessionControlClient
    {
        public List<LocalSessionControlRequest> Requests { get; } =
            [];

        public Task<LocalSessionControlResponse> SendAsync(
            LocalSessionControlRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(
                request);

            return Task.FromResult(
                new LocalSessionControlResponse(
                    true,
                    null,
                    new SessionStatus(
                        AgentAccessMode.Full,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.AddMinutes(
                            7),
                        OtpActive:
                            false,
                        OtpExpiresAtUtc:
                            null)));
        }
    }
}
