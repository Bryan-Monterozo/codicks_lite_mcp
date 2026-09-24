using System.ComponentModel;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Identity;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Workspaces;
using LocalAgent.Host.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class ServerTools
{
    [McpServerTool(Name = "server_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns identity, runtime, and non-secret Chunk 02 configuration status for the local Codicks Lite MCP host. Does not modify the computer.")]
    public static ServerInfoResponse ServerInfo(
        IOptions<AgentConfiguration> options,
        IWorkspaceRegistry workspaceRegistry,
        IUserPathResolver pathResolver,
        ConfigurationRuntimeInfo runtimeInfo)
    {
        var configuration = options.Value;
        var machineId = ResolveAutomaticValue(configuration.Agent.Machine.Id, Environment.MachineName);
        var machineDisplayName = ResolveAutomaticValue(configuration.Agent.Machine.DisplayName, Environment.MachineName);

        return new ServerInfoResponse(
            AgentIdentity.Current,
            new ServerConfigurationResponse(
                configuration.SchemaVersion,
                configuration.Agent.ReadOnly,
                machineId,
                machineDisplayName,
                workspaceRegistry.Count,
                pathResolver.Resolve(configuration.Agent.StateDirectory),
                runtimeInfo.ConfigFilePath,
                runtimeInfo.ReloadMode));
    }

    private static string ResolveAutomaticValue(string configuredValue, string fallback)
    {
        return configuredValue.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : configuredValue;
    }
}
