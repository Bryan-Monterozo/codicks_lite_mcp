using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class LocalSessionControlProcessorTests
{
    [Fact]
    public void Status_ReturnsLockedState_WithoutOtpSecret()
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        var response =
            fixture.Processor.Process(
                """{"action":"status"}""");

        using var json =
            JsonDocument.Parse(
                response);

        Assert.True(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.Equal(
            "Locked",
            json.RootElement
                .GetProperty(
                    "status")
                .GetProperty(
                    "mode")
                .GetString());

        Assert.True(
            json.RootElement
                .GetProperty(
                    "status")
                .GetProperty(
                    "otpActive")
                .GetBoolean());

        Assert.DoesNotContain(
            "123456",
            response,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockFull_UsesSameSessionGuardStateMachine()
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        var response =
            fixture.Processor.Process(
                """
                {"action":"unlock","mode":"Full","otp":"123456","leaseMinutes":5}
                """);

        using var json =
            JsonDocument.Parse(
                response);

        Assert.True(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.Equal(
            "Full",
            json.RootElement
                .GetProperty(
                    "status")
                .GetProperty(
                    "mode")
                .GetString());

        Assert.True(
            fixture.Guard
                .AuthorizeMutation()
                .Allowed);

        Assert.DoesNotContain(
            "123456",
            response,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnlockReadOnly_AllowsReadsAndDeniesMutations()
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        var response =
            fixture.Processor.Process(
                """
                {"action":"unlock","mode":"ReadOnly","otp":"123456"}
                """);

        using var json =
            JsonDocument.Parse(
                response);

        Assert.True(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.True(
            fixture.Guard
                .AuthorizeRead()
                .Allowed);

        Assert.False(
            fixture.Guard
                .AuthorizeMutation()
                .Allowed);

        Assert.Equal(
            "SESSION_READ_ONLY",
            fixture.Guard
                .AuthorizeMutation()
                .ErrorCode);
    }

    [Fact]
    public void WrongOtp_IsRejected_WithoutLeakingOtp()
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        var response =
            fixture.Processor.Process(
                """
                {"action":"unlock","mode":"Full","otp":"999999","leaseMinutes":5}
                """);

        using var json =
            JsonDocument.Parse(
                response);

        Assert.False(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.Equal(
            "OTP is invalid or expired.",
            json.RootElement
                .GetProperty(
                    "error")
                .GetString());

        Assert.Equal(
            AgentAccessMode.Locked,
            fixture.Guard
                .GetStatus()
                .Mode);

        Assert.DoesNotContain(
            "123456",
            response,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "999999",
            response,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void InvalidRequest_ReturnsStableFailure(
        string request)
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        var response =
            fixture.Processor.Process(
                request);

        using var json =
            JsonDocument.Parse(
                response);

        Assert.False(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());
    }

    [Fact]
    public void UnknownAction_IsRejected()
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        var response =
            fixture.Processor.Process(
                """{"action":"delete-the-internet"}""");

        using var json =
            JsonDocument.Parse(
                response);

        Assert.False(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.Equal(
            "Unknown action.",
            json.RootElement
                .GetProperty(
                    "error")
                .GetString());
    }

    [Fact]
    public void Lock_RotatesBackToLockedWithActiveOtp()
    {
        var fixture =
            CreateFixture(
                "123456",
                "654321");

        _ =
            fixture.Processor.Process(
                """
                {"action":"unlock","mode":"Full","otp":"123456","leaseMinutes":5}
                """);

        var response =
            fixture.Processor.Process(
                """{"action":"lock"}""");

        using var json =
            JsonDocument.Parse(
                response);

        Assert.True(
            json.RootElement
                .GetProperty(
                    "success")
                .GetBoolean());

        Assert.Equal(
            "Locked",
            json.RootElement
                .GetProperty(
                    "status")
                .GetProperty(
                    "mode")
                .GetString());

        Assert.True(
            json.RootElement
                .GetProperty(
                    "status")
                .GetProperty(
                    "otpActive")
                .GetBoolean());

        Assert.False(
            fixture.Guard
                .AuthorizeRead()
                .Allowed);
    }

    private static Fixture CreateFixture(
        params string[] otps)
    {
        var configuration =
            new AgentConfiguration
            {
                SessionSecurity =
                    new SessionSecurityOptions
                    {
                        DefaultMode =
                            AgentAccessMode.Locked,
                        DefaultLeaseMinutes =
                            30,
                        OtpDigits =
                            6,
                        OtpLifetimeMinutes =
                            10
                    }
            };

        var guard =
            new SessionGuard(
                configuration,
                new SequenceOtpGenerator(
                    otps),
                TimeProvider.System);

        return new Fixture(
            guard,
            new LocalSessionControlProcessor(
                guard));
    }

    private sealed record Fixture(
        SessionGuard Guard,
        LocalSessionControlProcessor Processor);

    private sealed class SequenceOtpGenerator(
        params string[] values) :
        ISessionOtpGenerator
    {
        private readonly Queue<string> _values =
            new(
                values);

        public string Generate(
            int digits)
        {
            Assert.True(
                _values.Count > 0);

            var value =
                _values.Dequeue();

            Assert.Equal(
                digits,
                value.Length);

            return value;
        }
    }
}
