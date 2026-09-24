using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Workspaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalAgent.Host.Configuration;

public sealed partial class ConfigurationStartupService(
    IOptions<AgentConfiguration> options,
    IWorkspaceRegistry workspaceRegistry,
    IUserPathResolver pathResolver,
    ConfigurationRuntimeInfo runtimeInfo,
    ILogger<ConfigurationStartupService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var stateDirectory = pathResolver.Resolve(configuration.Agent.StateDirectory);
        Directory.CreateDirectory(stateDirectory);

        LogConfigurationLoaded(
            logger,
            runtimeInfo.ConfigFilePath,
            configuration.SchemaVersion,
            workspaceRegistry.Count,
            runtimeInfo.ReloadMode);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Chunk 02 configuration loaded from {ConfigFilePath}; schema {SchemaVersion}; workspaces {WorkspaceCount}; reload mode {ReloadMode}.")]
    private static partial void LogConfigurationLoaded(
        ILogger logger,
        string configFilePath,
        int schemaVersion,
        int workspaceCount,
        string reloadMode);
}
