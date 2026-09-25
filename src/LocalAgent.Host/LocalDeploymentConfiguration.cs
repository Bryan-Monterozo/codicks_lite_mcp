using System.Text.Json;
using LocalAgent.Core.Platform;

namespace LocalAgent.Host;

public sealed record LocalDeploymentConfiguration(
    string Profile,
    string TunnelId,
    string McpCommand,
    string SecretService,
    string HealthListenAddress,
    string TunnelProfileDirectory);

public sealed class LocalDeploymentConfigurationStore(
    IPlatformApplicationPaths applicationPaths)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(
            JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public string ConfigPath =>
        Path.Combine(
            applicationPaths.ApplicationRoot,
            "config",
            "deployment.json");

    public LocalDeploymentConfiguration LoadOrCreate()
    {
        if (!File.Exists(
                ConfigPath))
        {
            var defaults =
                CreateDefaults();

            Save(
                defaults);

            return defaults;
        }

        try
        {
            var bytes =
                File.ReadAllBytes(
                    ConfigPath);

            return JsonSerializer.Deserialize<LocalDeploymentConfiguration>(
                       bytes,
                       JsonOptions)
                   ?? throw new InvalidDataException(
                       "Deployment configuration is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Deployment configuration JSON is invalid.",
                exception);
        }
    }

    public LocalDeploymentConfiguration Load()
    {
        if (!File.Exists(
                ConfigPath))
        {
            throw new FileNotFoundException(
                "Deployment configuration was not found. Run setup/config first.",
                ConfigPath);
        }

        return LoadOrCreate();
    }

    public void Save(
        LocalDeploymentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(
            configuration);

        var directory =
            Path.GetDirectoryName(
                ConfigPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new IOException(
                "Deployment configuration directory could not be determined.");
        }

        Directory.CreateDirectory(
            directory);

        var bytes =
            JsonSerializer.SerializeToUtf8Bytes(
                configuration,
                JsonOptions);

        WriteAtomically(
            ConfigPath,
            bytes);
    }

    private static void WriteAtomically(
        string destinationPath,
        byte[] bytes)
    {
        var directory =
            Path.GetDirectoryName(
                destinationPath);

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new IOException(
                "Deployment configuration directory could not be determined.");
        }

        var tempPath =
            Path.Combine(
                directory,
                $".deployment-{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream =
                   new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16_384,
                       FileOptions.WriteThrough))
            {
                stream.Write(
                    bytes);

                stream.Flush(
                    flushToDisk: true);
            }

            File.Move(
                tempPath,
                destinationPath,
                overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(
                    tempPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static string GetProfilePath(
        LocalDeploymentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(
            configuration);

        return Path.Combine(
            configuration.TunnelProfileDirectory,
            $"{configuration.Profile}.yaml");
    }

    private LocalDeploymentConfiguration CreateDefaults()
    {
        var userProfile =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        var profileDirectory =
            Environment.GetEnvironmentVariable(
                "CODICKS_LITE_TUNNEL_PROFILE_DIR");

        if (string.IsNullOrWhiteSpace(
                profileDirectory))
        {
            profileDirectory =
                Path.Combine(
                    userProfile,
                    ".config",
                    "tunnel-client");
        }

        var processPath =
            Environment.ProcessPath;

        var mcpCommand =
            string.IsNullOrWhiteSpace(
                processPath)
                ? Path.Combine(
                    applicationPaths.ApplicationRoot,
                    "current",
                    OperatingSystem.IsWindows()
                        ? "LocalAgent.Host.exe"
                        : "LocalAgent.Host")
                : processPath;

        return new LocalDeploymentConfiguration(
            Profile:
                "codicks-lite-local-stdio",
            TunnelId:
                "tunnel_REPLACE_ME",
            McpCommand:
                mcpCommand,
            SecretService:
                "CodicksLiteMcp.OpenAI.RuntimeApiKey",
            HealthListenAddress:
                "127.0.0.1:0",
            TunnelProfileDirectory:
                Path.GetFullPath(
                    profileDirectory));
    }
}
