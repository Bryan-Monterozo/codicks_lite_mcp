namespace LocalAgent.Core.Configuration;

public sealed class ExecutionOptions
{
    public bool Enabled { get; set; }

    public int MaxTimeoutSeconds { get; set; } = 600;

    public int MaxOutputBytes { get; set; } = 262_144;

    public Dictionary<string, ExecutableExecutionOptions> Executables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExecutableExecutionOptions
{
    public bool Enabled { get; set; }

    public bool AllowAnyArguments { get; set; }

    public List<string> AllowedCommands { get; set; } = [];

    public List<string> DeniedCommands { get; set; } = [];

    public int? MaxTimeoutSeconds { get; set; }
}
