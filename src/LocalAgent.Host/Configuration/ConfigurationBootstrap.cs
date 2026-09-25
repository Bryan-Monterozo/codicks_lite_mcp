using LocalAgent.Core.Platform;
using Microsoft.Extensions.Configuration;

namespace LocalAgent.Host.Configuration;

public static class ConfigurationBootstrap
{
    public const string ConfigFileEnvironmentVariable = "CODICKS_LITE_CONFIG_FILE";
    public const string EnvironmentVariablePrefix = "CODICKS_LITE_";

    public static ConfigurationRuntimeInfo Configure(
        ConfigurationManager configuration,
        IPlatformApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(applicationPaths);

        var packagedDefaultsPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "appsettings.json");

        var configFilePath =
            ResolveLiveConfigPath(
                applicationPaths);

        configuration.Sources.Clear();
        configuration.SetBasePath(
            AppContext.BaseDirectory);

        configuration.AddJsonFile(
            "appsettings.json",
            optional: true,
            reloadOnChange: false);

        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Agent:StateDirectory"] =
                    applicationPaths.DefaultStateDirectory
            });

        configuration.AddJsonFile(
            configFilePath,
            optional: true,
            reloadOnChange: false);

        configuration.AddEnvironmentVariables(
            EnvironmentVariablePrefix);

        return new ConfigurationRuntimeInfo(
            PackagedDefaultsPath: packagedDefaultsPath,
            ConfigFilePath: configFilePath,
            EnvironmentVariablePrefix: EnvironmentVariablePrefix,
            ReloadMode: "restart-required");
    }

    private static string ResolveLiveConfigPath(
        IPlatformApplicationPaths applicationPaths)
    {
        var configured =
            Environment.GetEnvironmentVariable(
                ConfigFileEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(
                configured))
        {
            return ExpandHomeAndNormalize(
                configured);
        }

        return applicationPaths.DefaultConfigFilePath;
    }

    private static string ExpandHomeAndNormalize(
        string configuredPath)
    {
        var path =
            configuredPath.Trim();

        var home =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

        if (path == "~")
        {
            path = home;
        }
        else
        {
            var homePrefix =
                "~" +
                Path.DirectorySeparatorChar;

            if (path.StartsWith(
                    homePrefix,
                    StringComparison.Ordinal))
            {
                path =
                    Path.Combine(
                        home,
                        path[homePrefix.Length..]);
            }
        }

        return Path.GetFullPath(
            path);
    }
}
