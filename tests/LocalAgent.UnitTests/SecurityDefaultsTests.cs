using LocalAgent.Core.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class SecurityDefaultsTests
{
    [Fact]
    public void EffectiveDenyGlobs_AlwaysContainMandatorySafeguards()
    {
        var effective = AgentSecurityDefaults.GetEffectiveDenyGlobs([]);

        Assert.Contains("**/.git/**", effective);
        Assert.Contains("**/.env", effective);
        Assert.Contains("**/*.key", effective);
    }
}
