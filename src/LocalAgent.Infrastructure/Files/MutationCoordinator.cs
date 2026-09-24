namespace LocalAgent.Infrastructure.Files;

internal static class MutationCoordinator
{
    public static object SyncRoot { get; } = new();
}
