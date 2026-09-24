using System.Text.Json.Serialization;
using LocalAgent.Core.Identity;

namespace LocalAgent.Host.Mcp;

public sealed record ServerInfoResponse(
    [property: JsonPropertyName("agent")] AgentIdentity Agent,
    [property: JsonPropertyName("configuration")] ServerConfigurationResponse Configuration);

public sealed record ServerConfigurationResponse(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("readOnly")] bool ReadOnly,
    [property: JsonPropertyName("machineId")] string MachineId,
    [property: JsonPropertyName("machineDisplayName")] string MachineDisplayName,
    [property: JsonPropertyName("workspaceCount")] int WorkspaceCount,
    [property: JsonPropertyName("stateDirectory")] string StateDirectory,
    [property: JsonPropertyName("configFilePath")] string ConfigFilePath,
    [property: JsonPropertyName("reloadMode")] string ReloadMode);

public sealed record SecurityStatusResponse(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("leaseExpiresAt")] DateTimeOffset? LeaseExpiresAt,
    [property: JsonPropertyName("otpActive")] bool OtpActive,
    [property: JsonPropertyName("otpExpiresAt")] DateTimeOffset? OtpExpiresAt);

public sealed record ScratchReadProbeResponse(
    [property: JsonPropertyName("exists")] bool Exists,
    [property: JsonPropertyName("content")] string? Content);
