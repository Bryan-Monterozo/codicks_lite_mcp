using System.ComponentModel;
using LocalAgent.Core.Security;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class SecurityTools
{
    [McpServerTool(
        Name = "security_status",
        Title = "Get Codicks Lite session security status",
        UseStructuredContent = true,
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns the current local session authorization mode and expiry state. Never returns the OTP and cannot change session mode.")]
    public static SecurityStatusResponse SecurityStatus(
        ISessionGuard sessionGuard)
    {
        ArgumentNullException.ThrowIfNull(sessionGuard);

        var status = sessionGuard.GetStatus();
        return new SecurityStatusResponse(
            status.Mode.ToString(),
            status.StartedAtUtc,
            status.LeaseExpiresAtUtc,
            status.OtpActive,
            status.OtpExpiresAtUtc);
    }
}
