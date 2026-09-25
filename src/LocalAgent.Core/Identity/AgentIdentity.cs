using System.Runtime.InteropServices;

namespace LocalAgent.Core.Identity;

public sealed record AgentIdentity(
    string Name,
    string Version,
    string MachineName,
    string OperatingSystem,
    string Architecture,
    string RuntimeVersion)
{
    public static AgentIdentity Current => new(
        Name: "Codicks Lite MCP",
        Version: "1.2.2",
        MachineName: Environment.MachineName,
        OperatingSystem: RuntimeInformation.OSDescription,
        Architecture: RuntimeInformation.OSArchitecture.ToString(),
        RuntimeVersion: RuntimeInformation.FrameworkDescription);
}
