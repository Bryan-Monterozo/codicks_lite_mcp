namespace LocalAgent.Core.Security;

public interface ISessionGuard
{
    SessionStatus GetStatus();

    SessionAuthorizationResult AuthorizeRead();

    SessionAuthorizationResult AuthorizeMutation();

    SessionAuthorizationResult AuthorizeFullAccess();

    bool TryUnlock(
        AgentAccessMode mode,
        string otp,
        int? leaseMinutes,
        out string? errorMessage);

    SessionStatus Lock();
}
