namespace LocalAgent.Host.Configuration;

public sealed record ConfigurationRuntimeInfo(
    string PackagedDefaultsPath,
    string ConfigFilePath,
    string EnvironmentVariablePrefix,
    string ReloadMode);
