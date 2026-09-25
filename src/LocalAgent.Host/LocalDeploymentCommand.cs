using LocalAgent.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAgent.Host;

public static class LocalDeploymentCommand
{
    private const string Flag =
        "--local-deployment";

    public static async Task<bool> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            args);

        ArgumentNullException.ThrowIfNull(
            services);

        if (args.Length == 0 ||
            !string.Equals(
                args[0],
                Flag,
                StringComparison.Ordinal))
        {
            return false;
        }

        Environment.ExitCode =
            0;

        if (args.Length < 2)
        {
            Fail(
                "Usage: --local-deployment <init|configure|show|apply|doctor|run|all|path> [options]",
                2);

            return true;
        }

        DeploymentArguments parsed;

        try
        {
            parsed =
                DeploymentArguments.Parse(
                    args[1..]);
        }
        catch (ArgumentException exception)
        {
            Fail(
                exception.Message,
                2);

            return true;
        }

        try
        {
            var store =
                services.GetRequiredService<
                    LocalDeploymentConfigurationStore>();

            var secretStore =
                services.GetRequiredService<
                    IPlatformSecretStore>();

            var runner =
                services.GetRequiredService<
                    ITunnelClientRunner>();

            switch (parsed.Action)
            {
                case "init":
                    Init(
                        store);

                    break;

                case "configure":
                    Configure(
                        parsed,
                        store,
                        secretStore);

                    break;

                case "show":
                    Show(
                        store.LoadOrCreate(),
                        store,
                        secretStore);

                    break;

                case "apply":
                    await ApplyAsync(
                        parsed,
                        store,
                        secretStore,
                        runner,
                        cancellationToken);

                    break;

                case "doctor":
                    await DoctorAsync(
                        store,
                        secretStore,
                        runner,
                        cancellationToken);

                    break;

                case "run":
                    await RunAsync(
                        store,
                        secretStore,
                        runner,
                        cancellationToken);

                    break;

                case "all":
                    Configure(
                        parsed,
                        store,
                        secretStore);

                    await ApplyAsync(
                        parsed with
                        {
                            Force = true
                        },
                        store,
                        secretStore,
                        runner,
                        cancellationToken);

                    await DoctorAsync(
                        store,
                        secretStore,
                        runner,
                        cancellationToken);

                    break;

                case "path":
                    Console.WriteLine(
                        store.ConfigPath);

                    break;

                default:
                    Fail(
                        $"Unknown deployment action: {parsed.Action}",
                        2);

                    break;
            }
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                InvalidDataException or
                InvalidOperationException or
                PlatformNotSupportedException or
                OperationCanceledException)
        {
            Fail(
                exception.Message,
                1);
        }

        return true;
    }

    private static void Init(
        LocalDeploymentConfigurationStore store)
    {
        _ =
            store.LoadOrCreate();

        Console.WriteLine(
            "Codicks Lite deployment configuration ready:");

        Console.WriteLine(
            $"  {store.ConfigPath}");
    }

    private static void Configure(
        DeploymentArguments arguments,
        LocalDeploymentConfigurationStore store,
        IPlatformSecretStore secretStore)
    {
        var current =
            store.LoadOrCreate();

        var configured =
            current with
            {
                Profile =
                    FirstNonEmpty(
                        arguments.Profile,
                        Environment.GetEnvironmentVariable(
                            "CODICKS_LITE_TUNNEL_PROFILE"),
                        current.Profile),
                TunnelId =
                    FirstNonEmpty(
                        arguments.TunnelId,
                        Environment.GetEnvironmentVariable(
                            "CONTROL_PLANE_TUNNEL_ID"),
                        Environment.GetEnvironmentVariable(
                            "CODICKS_LITE_TUNNEL_ID"),
                        current.TunnelId),
                McpCommand =
                    FirstNonEmpty(
                        arguments.McpCommand,
                        Environment.GetEnvironmentVariable(
                            "CODICKS_LITE_MCP_COMMAND"),
                        current.McpCommand),
                HealthListenAddress =
                    FirstNonEmpty(
                        arguments.HealthListenAddress,
                        Environment.GetEnvironmentVariable(
                            "CODICKS_LITE_HEALTH_LISTEN_ADDR"),
                        current.HealthListenAddress),
                TunnelProfileDirectory =
                    FirstNonEmpty(
                        arguments.ProfileDirectory,
                        Environment.GetEnvironmentVariable(
                            "CODICKS_LITE_TUNNEL_PROFILE_DIR"),
                        current.TunnelProfileDirectory)
            };

        if (!arguments.NonInteractive)
        {
            configured =
                configured with
                {
                    Profile =
                        PromptValue(
                            "Tunnel profile",
                            configured.Profile),
                    TunnelId =
                        PromptValue(
                            "Tunnel ID",
                            configured.TunnelId),
                    McpCommand =
                        PromptValue(
                            "MCP command",
                            configured.McpCommand),
                    HealthListenAddress =
                        PromptValue(
                            "Health listener",
                            configured.HealthListenAddress),
                    TunnelProfileDirectory =
                        PromptValue(
                            "Tunnel profile directory",
                            configured.TunnelProfileDirectory)
                };
        }

        configured =
            configured with
            {
                TunnelProfileDirectory =
                    Path.GetFullPath(
                        configured.TunnelProfileDirectory),
                McpCommand =
                    Path.GetFullPath(
                        configured.McpCommand)
            };

        ValidateConfiguration(
            configured);

        store.Save(
            configured);

        var runtimeKey =
            FirstNonEmpty(
                arguments.RuntimeKey,
                Environment.GetEnvironmentVariable(
                    "CONTROL_PLANE_API_KEY"));

        if (!string.IsNullOrWhiteSpace(
                runtimeKey))
        {
            ValidateRuntimeKey(
                runtimeKey);

            secretStore.Store(
                configured.SecretService,
                runtimeKey);
        }
        else if (!secretStore.Contains(
                     configured.SecretService))
        {
            if (arguments.NonInteractive ||
                Console.IsInputRedirected)
            {
                throw new InvalidOperationException(
                    "No runtime API key was supplied and none exists in the platform secret store.");
            }

            runtimeKey =
                ReadSecret(
                    "OpenAI runtime API key");

            ValidateRuntimeKey(
                runtimeKey);

            secretStore.Store(
                configured.SecretService,
                runtimeKey);
        }

        Console.WriteLine(
            "Codicks Lite deployment configuration saved.");

        Show(
            configured,
            store,
            secretStore);
    }

    private static void Show(
        LocalDeploymentConfiguration configuration,
        LocalDeploymentConfigurationStore store,
        IPlatformSecretStore secretStore)
    {
        var environmentKey =
            Environment.GetEnvironmentVariable(
                "CONTROL_PLANE_API_KEY");

        var secretStatus =
            !string.IsNullOrWhiteSpace(
                environmentKey)
                ? "PRESENT IN ENVIRONMENT"
                : secretStore.Contains(
                    configuration.SecretService)
                    ? "PRESENT IN PLATFORM SECRET STORE"
                    : "MISSING";

        Console.WriteLine();
        Console.WriteLine(
            "Codicks Lite deployment configuration");

        Console.WriteLine(
            "--------------------------------");

        Console.WriteLine(
            $"Config file: {store.ConfigPath}");

        Console.WriteLine(
            $"Tunnel profile: {configuration.Profile}");

        Console.WriteLine(
            $"Tunnel ID: {configuration.TunnelId}");

        Console.WriteLine(
            $"MCP command: {configuration.McpCommand}");

        Console.WriteLine(
            $"Health listener: {configuration.HealthListenAddress}");

        Console.WriteLine(
            $"Tunnel profile file: {LocalDeploymentConfigurationStore.GetProfilePath(configuration)}");

        Console.WriteLine(
            $"Runtime key: {secretStatus}");

        Console.WriteLine(
            $"Secret service: {configuration.SecretService}");

        Console.WriteLine();
        Console.WriteLine(
            "Secrets are not written into deployment.json or the tunnel profile.");
    }

    private static async Task ApplyAsync(
        DeploymentArguments arguments,
        LocalDeploymentConfigurationStore store,
        IPlatformSecretStore secretStore,
        ITunnelClientRunner runner,
        CancellationToken cancellationToken)
    {
        var configuration =
            store.Load();

        ValidateConfiguration(
            configuration);

        var runtimeKey =
            LoadRuntimeKey(
                configuration,
                secretStore);

        var profilePath =
            LocalDeploymentConfigurationStore.GetProfilePath(
                configuration);

        if (File.Exists(
                profilePath) &&
            !arguments.Force &&
            !arguments.NonInteractive)
        {
            if (Console.IsInputRedirected)
            {
                throw new InvalidOperationException(
                    "Tunnel profile already exists; use --force for non-interactive recreation.");
            }

            Console.Write(
                "Tunnel profile already exists. Recreate it? [y/N]: ");

            var response =
                Console.ReadLine();

            if (!IsYes(
                    response))
            {
                Console.WriteLine(
                    "Profile recreation cancelled.");

                return;
            }
        }

        BackupProfileIfPresent(
            profilePath,
            store);

        var exitCode =
            await runner.RunAsync(
                [
                    "init",
                    "--sample",
                    "sample_mcp_stdio_local",
                    "--profile",
                    configuration.Profile,
                    "--tunnel-id",
                    configuration.TunnelId,
                    "--mcp-command",
                    configuration.McpCommand,
                    "--control-plane-api-key-ref",
                    "env:CONTROL_PLANE_API_KEY",
                    "--health-listen-addr",
                    configuration.HealthListenAddress,
                    "--force"
                ],
                runtimeKey,
                cancellationToken);

        if (exitCode !=
            0)
        {
            throw new IOException(
                $"tunnel-client init failed with exit code {exitCode}.");
        }

        Console.WriteLine(
            $"Tunnel profile initialized: {configuration.Profile}");
    }

    private static async Task DoctorAsync(
        LocalDeploymentConfigurationStore store,
        IPlatformSecretStore secretStore,
        ITunnelClientRunner runner,
        CancellationToken cancellationToken)
    {
        var configuration =
            store.Load();

        ValidateConfiguration(
            configuration);

        var runtimeKey =
            LoadRuntimeKey(
                configuration,
                secretStore);

        var exitCode =
            await runner.RunAsync(
                [
                    "doctor",
                    "--profile",
                    configuration.Profile,
                    "--explain"
                ],
                runtimeKey,
                cancellationToken);

        if (exitCode !=
            0)
        {
            throw new IOException(
                $"tunnel-client doctor failed with exit code {exitCode}.");
        }
    }

    private static async Task RunAsync(
        LocalDeploymentConfigurationStore store,
        IPlatformSecretStore secretStore,
        ITunnelClientRunner runner,
        CancellationToken cancellationToken)
    {
        var configuration =
            store.Load();

        ValidateConfiguration(
            configuration);

        var runtimeKey =
            LoadRuntimeKey(
                configuration,
                secretStore);

        var exitCode =
            await runner.RunAsync(
                [
                    "run",
                    "--profile",
                    configuration.Profile
                ],
                runtimeKey,
                cancellationToken);

        if (exitCode !=
            0)
        {
            throw new IOException(
                $"tunnel-client run exited with code {exitCode}.");
        }
    }

    private static void BackupProfileIfPresent(
        string profilePath,
        LocalDeploymentConfigurationStore store)
    {
        if (!File.Exists(
                profilePath))
        {
            return;
        }

        var configDirectory =
            Path.GetDirectoryName(
                store.ConfigPath)
            ?? throw new IOException(
                "Deployment configuration directory is invalid.");

        var applicationRoot =
            Directory.GetParent(
                    configDirectory)
                ?.FullName
            ?? throw new IOException(
                "Application root could not be determined.");

        var backupDirectory =
            Path.Combine(
                applicationRoot,
                "profile-backups");

        Directory.CreateDirectory(
            backupDirectory);

        var backupPath =
            Path.Combine(
                backupDirectory,
                $"{Path.GetFileName(profilePath)}.{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.bak");

        File.Copy(
            profilePath,
            backupPath,
            overwrite: false);

        Console.WriteLine(
            $"Existing tunnel profile backed up to: {backupPath}");
    }

    private static string LoadRuntimeKey(
        LocalDeploymentConfiguration configuration,
        IPlatformSecretStore secretStore)
    {
        var environmentKey =
            Environment.GetEnvironmentVariable(
                "CONTROL_PLANE_API_KEY");

        if (!string.IsNullOrWhiteSpace(
                environmentKey))
        {
            ValidateRuntimeKey(
                environmentKey);

            return environmentKey;
        }

        var stored =
            secretStore.Read(
                configuration.SecretService);

        if (string.IsNullOrWhiteSpace(
                stored))
        {
            throw new InvalidOperationException(
                "No runtime API key exists in the platform secret store or environment.");
        }

        ValidateRuntimeKey(
            stored);

        return stored;
    }

    private static void ValidateConfiguration(
        LocalDeploymentConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(
                configuration.Profile) ||
            configuration.Profile.Any(
                character =>
                    !char.IsLetterOrDigit(
                        character) &&
                    character is not
                        ('.' or '_' or '-')))
        {
            throw new ArgumentException(
                "Tunnel profile may contain only letters, digits, dot, underscore, or hyphen.");
        }

        if (IsPlaceholder(
                configuration.TunnelId) ||
            !configuration.TunnelId.StartsWith(
                "tunnel_",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Tunnel ID must be configured and begin with 'tunnel_'.");
        }

        if (!Path.IsPathRooted(
                configuration.McpCommand) ||
            !File.Exists(
                configuration.McpCommand))
        {
            throw new ArgumentException(
                $"MCP command must be an existing absolute file: {configuration.McpCommand}");
        }

        if (string.IsNullOrWhiteSpace(
                configuration.HealthListenAddress))
        {
            throw new ArgumentException(
                "Health listener is required.");
        }

        if (string.IsNullOrWhiteSpace(
                configuration.TunnelProfileDirectory) ||
            !Path.IsPathRooted(
                configuration.TunnelProfileDirectory))
        {
            throw new ArgumentException(
                "Tunnel profile directory must be an absolute path.");
        }
    }

    private static void ValidateRuntimeKey(
        string runtimeKey)
    {
        if (IsPlaceholder(
                runtimeKey))
        {
            throw new ArgumentException(
                "Runtime API key is empty or still a placeholder.");
        }
    }

    private static bool IsPlaceholder(
        string? value) =>
        string.IsNullOrWhiteSpace(
            value) ||
        value.Contains(
            "REPLACE_ME",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "YOUR_",
            StringComparison.OrdinalIgnoreCase);

    private static string PromptValue(
        string label,
        string current)
    {
        Console.Write(
            $"{label} [{current}]: ");

        var response =
            Console.ReadLine();

        return string.IsNullOrWhiteSpace(
                response)
            ? current
            : response.Trim();
    }

    private static string ReadSecret(
        string label)
    {
        Console.Error.Write(
            $"{label} (input hidden): ");

        var characters =
            new List<char>();

        while (true)
        {
            var key =
                Console.ReadKey(
                    intercept: true);

            if (key.Key ==
                ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key ==
                ConsoleKey.Backspace)
            {
                if (characters.Count >
                    0)
                {
                    characters.RemoveAt(
                        characters.Count -
                        1);
                }

                continue;
            }

            characters.Add(
                key.KeyChar);
        }

        Console.Error.WriteLine();

        return new string(
            characters.ToArray());
    }

    private static bool IsYes(
        string? value) =>
        string.Equals(
            value,
            "y",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            value,
            "yes",
            StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(
        params string?[] values) =>
        values.First(
            value =>
                !string.IsNullOrWhiteSpace(
                    value))!;

    private static void Fail(
        string message,
        int exitCode)
    {
        Console.Error.WriteLine(
            $"ERROR: {message}");

        Environment.ExitCode =
            exitCode;
    }

    private sealed record DeploymentArguments(
        string Action,
        bool NonInteractive,
        bool Force,
        string? TunnelId,
        string? RuntimeKey,
        string? Profile,
        string? McpCommand,
        string? HealthListenAddress,
        string? ProfileDirectory)
    {
        public static DeploymentArguments Parse(
            string[] args)
        {
            if (args.Length ==
                0)
            {
                throw new ArgumentException(
                    "Deployment action is required.");
            }

            var action =
                args[0]
                    .Trim()
                    .ToLowerInvariant();

            var nonInteractive =
                false;

            var force =
                false;

            string? tunnelId =
                null;

            string? runtimeKey =
                null;

            string? profile =
                null;

            string? mcpCommand =
                null;

            string? healthListenAddress =
                null;

            string? profileDirectory =
                null;

            for (var index = 1;
                 index < args.Length;
                 index++)
            {
                switch (args[index])
                {
                    case "--non-interactive":
                        nonInteractive =
                            true;

                        break;

                    case "--force":
                        force =
                            true;

                        break;

                    case "--tunnel-id":
                        tunnelId =
                            ReadValue(
                                args,
                                ref index,
                                "--tunnel-id");

                        break;

                    case "--runtime-key":
                        runtimeKey =
                            ReadValue(
                                args,
                                ref index,
                                "--runtime-key");

                        break;

                    case "--profile":
                        profile =
                            ReadValue(
                                args,
                                ref index,
                                "--profile");

                        break;

                    case "--mcp-command":
                        mcpCommand =
                            ReadValue(
                                args,
                                ref index,
                                "--mcp-command");

                        break;

                    case "--health-listen-addr":
                        healthListenAddress =
                            ReadValue(
                                args,
                                ref index,
                                "--health-listen-addr");

                        break;

                    case "--profile-dir":
                        profileDirectory =
                            ReadValue(
                                args,
                                ref index,
                                "--profile-dir");

                        break;

                    default:
                        throw new ArgumentException(
                            $"Unknown deployment argument: {args[index]}");
                }
            }

            return new DeploymentArguments(
                action,
                nonInteractive,
                force,
                tunnelId,
                runtimeKey,
                profile,
                mcpCommand,
                healthListenAddress,
                profileDirectory);
        }

        private static string ReadValue(
            string[] args,
            ref int index,
            string option)
        {
            if (index + 1 >=
                args.Length)
            {
                throw new ArgumentException(
                    $"{option} requires a value.");
            }

            return args[++index];
        }
    }
}
