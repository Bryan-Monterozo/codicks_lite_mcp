namespace LocalAgent.Core.Security;

public sealed class SessionSecurityOptions
{
    public AgentAccessMode DefaultMode { get; set; } = AgentAccessMode.Locked;

    public int DefaultLeaseMinutes { get; set; } = 30;

    public int OtpDigits { get; set; } = 6;

    public int OtpLifetimeMinutes { get; set; } = 10;
}
