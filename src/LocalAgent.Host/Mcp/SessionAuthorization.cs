using LocalAgent.Core.Security;
using ModelContextProtocol;

namespace LocalAgent.Host.Mcp;

internal static class SessionAuthorization
{
    public static void RequireRead(ISessionGuard sessionGuard) =>
        Require(sessionGuard, mutation: false);

    public static void RequireMutation(ISessionGuard sessionGuard) =>
        Require(sessionGuard, mutation: true);

    public static void RequireFull(ISessionGuard sessionGuard)
    {
        ArgumentNullException.ThrowIfNull(sessionGuard);
        ThrowIfDenied(sessionGuard.AuthorizeFullAccess());
    }

    private static void Require(
        ISessionGuard sessionGuard,
        bool mutation)
    {
        ArgumentNullException.ThrowIfNull(sessionGuard);

        var result = mutation
            ? sessionGuard.AuthorizeMutation()
            : sessionGuard.AuthorizeRead();

        ThrowIfDenied(result);
    }

    private static void ThrowIfDenied(
        SessionAuthorizationResult result)
    {
        if (result.Allowed)
        {
            return;
        }

        throw new McpException(
            $"{result.ErrorCode}: {result.Message}");
    }
}
