namespace LocalAgent.Core.Security;

public static class AgentSecurityDefaults
{
    public static IReadOnlyList<string> MandatoryDenyGlobs { get; } =
    [
        "**/.git",
        "**/.git/**",
        "**/.env",
        "**/.env.*",
        "**/*.pfx",
        "**/*.p12",
        "**/*.key",
        "**/.codicks-lite-recovery",
        "**/.codicks-lite-recovery/**"
    ];

    public static IReadOnlyList<string> GetEffectiveDenyGlobs(IEnumerable<string> configuredDenyGlobs)
    {
        ArgumentNullException.ThrowIfNull(configuredDenyGlobs);

        return MandatoryDenyGlobs
            .Concat(configuredDenyGlobs)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
