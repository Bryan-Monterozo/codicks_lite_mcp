namespace LocalAgent.Core.Security;

public sealed record SessionAuthorizationResult(
    bool Allowed,
    string? ErrorCode,
    string? Message)
{
    public static SessionAuthorizationResult Allow() =>
        new(true, null, null);

    public static SessionAuthorizationResult Deny(
        string errorCode,
        string message) =>
        new(false, errorCode, message);
}
