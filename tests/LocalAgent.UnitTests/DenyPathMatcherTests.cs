using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class DenyPathMatcherTests
{
    private static readonly string[] Patterns =
    [
        "**/.git",
        "**/.git/**",
        "**/.env",
        "**/.env.*",
        "**/*.key",
        "private/**"
    ];

    [Theory]
    [InlineData(".env")]
    [InlineData("config/.env.local")]
    [InlineData(".git/config")]
    [InlineData("src/nested/.git/HEAD")]
    [InlineData("certs/server.key")]
    [InlineData("private/notes.txt")]
    public void IsDenied_MatchesProtectedPaths(string path)
    {
        var matcher = new DenyPathMatcher(Patterns);

        var denied = matcher.IsDenied(path, out var matchedPattern);

        Assert.True(denied);
        Assert.False(string.IsNullOrWhiteSpace(matchedPattern));
    }

    [Fact]
    public void IsDenied_DoesNotMatchOrdinarySourceFile()
    {
        var matcher = new DenyPathMatcher(Patterns);

        var denied = matcher.IsDenied("src/Feature/Widget.cs", out var matchedPattern);

        Assert.False(denied);
        Assert.Null(matchedPattern);
    }
}
