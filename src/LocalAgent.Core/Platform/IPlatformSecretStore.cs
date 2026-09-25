namespace LocalAgent.Core.Platform;

public interface IPlatformSecretStore
{
    bool Contains(
        string serviceName);

    string? Read(
        string serviceName);

    void Store(
        string serviceName,
        string secret);

    bool Delete(
        string serviceName);
}
