namespace LocalAgent.Host;

public static class LocalCommandRouter
{
    public static async Task<bool> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            args);

        ArgumentNullException.ThrowIfNull(
            services);

        if (LocalBackupCommand.TryRun(
                args,
                services))
        {
            return true;
        }

        if (await LocalSessionCommand.TryRunAsync(
                args,
                services,
                cancellationToken))
        {
            return true;
        }

        if (await LocalDeploymentCommand.TryRunAsync(
                args,
                services,
                cancellationToken))
        {
            return true;
        }

        if (await LocalInfoCommand.TryRunAsync(
                args,
                services,
                cancellationToken))
        {
            return true;
        }

        return false;
    }
}
