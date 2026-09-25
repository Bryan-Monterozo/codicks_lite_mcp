using System.Text.Json;
using LocalAgent.Host.Configuration;
using LocalAgent.Infrastructure.Platform;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LocalAgent.IntegrationTests;

[CollectionDefinition(
    Name,
    DisableParallelization = true)]
public sealed class ConfigurationEnvironmentCollection
{
    public const string Name =
        "ConfigurationEnvironment";
}

[Collection(
    ConfigurationEnvironmentCollection.Name)]
public sealed class PlatformConfigurationIntegrationTests :
    IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot(
            "pcfg");

    [Fact]
    public void Configure_UsesPlatformStateDefault_WhenLiveConfigOmitsIt()
    {
        var paths =
            PlatformApplicationPaths.CreateWindows(
                Path.Combine(
                    _root,
                    "LocalAppData"));

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                paths.DefaultConfigFilePath)!);

        File.WriteAllText(
            paths.DefaultConfigFilePath,
            JsonSerializer.Serialize(
                new
                {
                    SchemaVersion = 1,
                    Agent = new
                    {
                        Workspaces =
                            new Dictionary<string, object>()
                    }
                }));

        var previous =
            Environment.GetEnvironmentVariable(
                ConfigurationBootstrap
                    .ConfigFileEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                ConfigurationBootstrap
                    .ConfigFileEnvironmentVariable,
                null);

            var configuration =
                new ConfigurationManager();

            var runtimeInfo =
                ConfigurationBootstrap.Configure(
                    configuration,
                    paths);

            Assert.Equal(
                paths.DefaultStateDirectory,
                configuration[
                    "Agent:StateDirectory"]);

            Assert.Equal(
                paths.DefaultConfigFilePath,
                runtimeInfo.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ConfigurationBootstrap
                    .ConfigFileEnvironmentVariable,
                previous);
        }
    }

    [Fact]
    public void Configure_LiveStateDirectory_OverridesPlatformDefault()
    {
        var paths =
            PlatformApplicationPaths.CreateWindows(
                Path.Combine(
                    _root,
                    "LocalAppDataOverride"));

        var explicitState =
            Path.Combine(
                _root,
                "explicit-state");

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                paths.DefaultConfigFilePath)!);

        File.WriteAllText(
            paths.DefaultConfigFilePath,
            JsonSerializer.Serialize(
                new
                {
                    SchemaVersion = 1,
                    Agent = new
                    {
                        StateDirectory =
                            explicitState,
                        Workspaces =
                            new Dictionary<string, object>()
                    }
                }));

        var previous =
            Environment.GetEnvironmentVariable(
                ConfigurationBootstrap
                    .ConfigFileEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                ConfigurationBootstrap
                    .ConfigFileEnvironmentVariable,
                null);

            var configuration =
                new ConfigurationManager();

            _ =
                ConfigurationBootstrap.Configure(
                    configuration,
                    paths);

            Assert.Equal(
                explicitState,
                configuration[
                    "Agent:StateDirectory"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ConfigurationBootstrap
                    .ConfigFileEnvironmentVariable,
                previous);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(
                _root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }
}
