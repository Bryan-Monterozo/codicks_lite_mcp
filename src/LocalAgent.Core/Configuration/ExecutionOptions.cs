namespace LocalAgent.Core.Configuration;

public sealed class ExecutionOptions
{
    public bool Enabled { get; set; }

    public int MaxTimeoutSeconds { get; set; } = 600;

    public int MaxOutputBytes { get; set; } = 262_144;

    public Dictionary<string, ExecutableExecutionOptions> Executables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public SandboxExecutionOptions Sandbox { get; set; } = new();
}

public sealed class ExecutableExecutionOptions
{
    public bool Enabled { get; set; }

    public bool AllowAnyArguments { get; set; }

    public List<string> AllowedCommands { get; set; } = [];

    public List<string> DeniedCommands { get; set; } = [];

    public int? MaxTimeoutSeconds { get; set; }
}


public sealed class SandboxExecutionOptions
{
    public bool Enabled { get; set; }

    public string RuntimeExecutable { get; set; } = "container";

    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";

    public int CpuCount { get; set; } = 2;

    public int MemoryMegabytes { get; set; } = 2_048;

    public int TmpfsMegabytes { get; set; } = 256;

    public bool ReadOnlyRoot { get; set; } = true;

    public bool NetworkEnabled { get; set; }
}
