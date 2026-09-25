namespace LocalAgent.Core.Platform;

public interface IPlatformApplicationPaths
{
    CodicksPlatformKind Platform { get; }

    string ApplicationRoot { get; }

    string DefaultStateDirectory { get; }

    string DefaultConfigFilePath { get; }
}
