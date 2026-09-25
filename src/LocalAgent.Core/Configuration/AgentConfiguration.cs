using LocalAgent.Core.Security;

namespace LocalAgent.Core.Configuration;

public sealed class AgentConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public AgentOptions Agent { get; set; } = new();

    public SessionSecurityOptions SessionSecurity { get; set; } = new();

    public ExecutionOptions Execution { get; set; } = new();
}

public sealed class AgentOptions
{
    public MachineOptions Machine { get; set; } = new();

    public bool ReadOnly { get; set; }

    public string StateDirectory { get; set; } = string.Empty;

    public AgentLimitsOptions Limits { get; set; } = new();

    public RecoveryOptions Recovery { get; set; } = new();

    public AgentLoggingOptions Logging { get; set; } = new();

    public AgentSecurityOptions Security { get; set; } = new();

    public AgentSearchOptions Search { get; set; } = new();

    public Dictionary<string, WorkspaceOptions> Workspaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MachineOptions
{
    public string Id { get; set; } = "auto";

    public string DisplayName { get; set; } = "auto";
}

public sealed class AgentLimitsOptions
{
    public long MaxEditableFileBytes { get; set; } = 1_048_576;

    public long MaxReadResponseBytes { get; set; } = 65_536;

    public int MaxDirectoryEntries { get; set; } = 200;

    public int MaxSearchResults { get; set; } = 100;

    public int MaxTraversalDepth { get; set; } = 12;

    public int OperationTimeoutSeconds { get; set; } = 15;

    public int MaxConcurrentMutations { get; set; } = 1;
}

public sealed class RecoveryOptions
{
    public int RetentionDays { get; set; } = 30;

    public long MaxStorageBytes { get; set; } = 1_073_741_824;
}

public sealed class AgentLoggingOptions
{
    public string MinimumLevel { get; set; } = "Information";
}

public sealed class AgentSecurityOptions
{
    public List<string> DenyGlobs { get; set; } = [];
}

public sealed class AgentSearchOptions
{
    public List<string> ExcludeGlobs { get; set; } = [];
}

public sealed class WorkspaceOptions
{
    public string Root { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<string> AllowedOperations { get; set; } = ["read"];
}
