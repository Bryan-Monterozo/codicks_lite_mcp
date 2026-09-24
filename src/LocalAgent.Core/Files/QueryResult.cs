using LocalAgent.Core.Security;

namespace LocalAgent.Core.Files;

public sealed record QueryResult<T>(
    bool Success,
    T? Value,
    FileQueryError Error,
    string Message,
    WorkspaceAccessError? AccessError = null)
    where T : class;

public static class QueryResult
{
    public static QueryResult<T> Ok<T>(T value)
        where T : class =>
        new(true, value, FileQueryError.None, string.Empty);

    public static QueryResult<T> Fail<T>(
        FileQueryError error,
        string message,
        WorkspaceAccessError? accessError = null)
        where T : class =>
        new(false, null, error, message, accessError);
}
