using LocalAgent.Core.Security;

namespace LocalAgent.Core.Files;

public sealed record MutationResult<T>(
    bool Success,
    T? Value,
    FileMutationError Error,
    string Message,
    WorkspaceAccessError? AccessError = null)
    where T : class;

public static class MutationResult
{
    public static MutationResult<T> Ok<T>(T value)
        where T : class =>
        new(true, value, FileMutationError.None, string.Empty);

    public static MutationResult<T> Fail<T>(
        FileMutationError error,
        string message,
        WorkspaceAccessError? accessError = null)
        where T : class =>
        new(false, null, error, message, accessError);
}
