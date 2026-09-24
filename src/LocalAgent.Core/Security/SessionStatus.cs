namespace LocalAgent.Core.Security;

public sealed record SessionStatus(
    AgentAccessMode Mode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? LeaseExpiresAtUtc,
    bool OtpActive,
    DateTimeOffset? OtpExpiresAtUtc);
