using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class SessionGuardTests
{
    [Fact]
    public void Startup_IsLocked_WithActiveConfiguredOtp()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 23, 5, 0, 0, TimeSpan.Zero));
        var guard = CreateGuard(clock, "123456");

        var status = guard.GetStatus();

        Assert.Equal(AgentAccessMode.Locked, status.Mode);
        Assert.True(status.OtpActive);
        Assert.Equal(clock.GetUtcNow().AddMinutes(10), status.OtpExpiresAtUtc);
        Assert.False(guard.AuthorizeRead().Allowed);
        Assert.Equal("SESSION_LOCKED", guard.AuthorizeRead().ErrorCode);
    }

    [Fact]
    public void CorrectOtp_UnlocksFull_AndIsOneTimeUse()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 23, 5, 0, 0, TimeSpan.Zero));
        var guard = CreateGuard(clock, "123456");

        var unlocked = guard.TryUnlock(
            AgentAccessMode.Full,
            "123456",
            leaseMinutes: 5,
            out var error);

        Assert.True(unlocked, error);
        Assert.True(guard.AuthorizeRead().Allowed);
        Assert.True(guard.AuthorizeMutation().Allowed);
        Assert.False(guard.GetStatus().OtpActive);

        guard.Lock();

        var reused = guard.TryUnlock(
            AgentAccessMode.Full,
            "123456",
            leaseMinutes: 5,
            out _);

        Assert.False(reused);
    }

    [Fact]
    public void ReadOnly_AllowsReads_AndDeniesMutations()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 23, 5, 0, 0, TimeSpan.Zero));
        var guard = CreateGuard(clock, "123456");

        Assert.True(
            guard.TryUnlock(
                AgentAccessMode.ReadOnly,
                "123456",
                leaseMinutes: null,
                out var error),
            error);

        Assert.True(guard.AuthorizeRead().Allowed);
        Assert.False(guard.AuthorizeMutation().Allowed);
        Assert.Equal(
            "SESSION_READ_ONLY",
            guard.AuthorizeMutation().ErrorCode);
    }

    [Fact]
    public void ExpiredOtp_IsRejected()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 23, 5, 0, 0, TimeSpan.Zero));
        var guard = CreateGuard(clock, "123456");

        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.False(
            guard.TryUnlock(
                AgentAccessMode.Full,
                "123456",
                leaseMinutes: 5,
                out _));
    }

    [Fact]
    public void LeaseExpiry_ReturnsToLocked_WithFreshOtp()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 23, 5, 0, 0, TimeSpan.Zero));
        var generator = new SequenceOtpGenerator(
            "123456",
            "654321");
        var configuration = CreateConfiguration();
        var guard = new SessionGuard(
            configuration,
            generator,
            clock);

        Assert.True(
            guard.TryUnlock(
                AgentAccessMode.Full,
                "123456",
                leaseMinutes: 1,
                out var error),
            error);

        clock.Advance(TimeSpan.FromMinutes(2));

        var status = guard.GetStatus();

        Assert.Equal(AgentAccessMode.Locked, status.Mode);
        Assert.True(status.OtpActive);

        Assert.True(
            guard.TryUnlock(
                AgentAccessMode.ReadOnly,
                "654321",
                leaseMinutes: 1,
                out error),
            error);
    }

    [Fact]
    public void WrongOtp_DoesNotUnlock()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 23, 5, 0, 0, TimeSpan.Zero));
        var guard = CreateGuard(clock, "123456");

        Assert.False(
            guard.TryUnlock(
                AgentAccessMode.Full,
                "123455",
                leaseMinutes: 5,
                out var error));

        Assert.Equal("OTP is invalid or expired.", error);
        Assert.Equal(AgentAccessMode.Locked, guard.GetStatus().Mode);
    }

    private static SessionGuard CreateGuard(
        TimeProvider clock,
        string otp) =>
        new(
            CreateConfiguration(),
            new SequenceOtpGenerator(otp, "654321"),
            clock);

    private static AgentConfiguration CreateConfiguration() =>
        new()
        {
            SessionSecurity = new SessionSecurityOptions
            {
                DefaultMode = AgentAccessMode.Locked,
                DefaultLeaseMinutes = 30,
                OtpDigits = 6,
                OtpLifetimeMinutes = 10
            }
        };

    private sealed class SequenceOtpGenerator(
        params string[] values) : ISessionOtpGenerator
    {
        private readonly Queue<string> _values = new(values);

        public string Generate(int digits)
        {
            Assert.True(_values.Count > 0);
            var value = _values.Dequeue();
            Assert.Equal(digits, value.Length);
            return value;
        }
    }

    private sealed class ManualTimeProvider(
        DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Advance(TimeSpan duration) =>
            _current = _current.Add(duration);
    }
}
