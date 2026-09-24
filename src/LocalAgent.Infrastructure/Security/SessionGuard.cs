using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.Security;

public sealed class SessionGuard : ISessionGuard
{
    private readonly object _sync = new();
    private readonly SessionSecurityOptions _options;
    private readonly ISessionOtpGenerator _otpGenerator;
    private readonly TimeProvider _timeProvider;

    private AgentAccessMode _mode = AgentAccessMode.Locked;
    private DateTimeOffset _startedAtUtc;
    private DateTimeOffset? _leaseExpiresAtUtc;
    private string _otp = string.Empty;
    private DateTimeOffset _otpExpiresAtUtc;
    private bool _otpConsumed;

    public SessionGuard(
        AgentConfiguration configuration,
        ISessionOtpGenerator otpGenerator,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(otpGenerator);

        _options = configuration.SessionSecurity;
        _otpGenerator = otpGenerator;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var now = _timeProvider.GetUtcNow();
        _startedAtUtc = now;
        GenerateFreshOtp(now);
    }

    public SessionStatus GetStatus()
    {
        lock (_sync)
        {
            RefreshExpiredLease();
            return CreateStatus();
        }
    }

    public SessionAuthorizationResult AuthorizeRead()
    {
        lock (_sync)
        {
            RefreshExpiredLease();

            return _mode switch
            {
                AgentAccessMode.ReadOnly or AgentAccessMode.Full =>
                    SessionAuthorizationResult.Allow(),
                _ =>
                    SessionAuthorizationResult.Deny(
                        "SESSION_LOCKED",
                        "Codicks Lite session is locked. Local authorization is required.")
            };
        }
    }

    public SessionAuthorizationResult AuthorizeMutation()
    {
        lock (_sync)
        {
            RefreshExpiredLease();

            return _mode switch
            {
                AgentAccessMode.Full =>
                    SessionAuthorizationResult.Allow(),
                AgentAccessMode.ReadOnly =>
                    SessionAuthorizationResult.Deny(
                        "SESSION_READ_ONLY",
                        "This session currently permits read-only operations."),
                _ =>
                    SessionAuthorizationResult.Deny(
                        "SESSION_LOCKED",
                        "Codicks Lite session is locked. Local authorization is required.")
            };
        }
    }

    public SessionAuthorizationResult AuthorizeFullAccess() =>
        AuthorizeMutation();

    public bool TryUnlock(
        AgentAccessMode mode,
        string otp,
        int? leaseMinutes,
        out string? errorMessage)
    {
        lock (_sync)
        {
            RefreshExpiredLease();

            if (mode is not (AgentAccessMode.ReadOnly or AgentAccessMode.Full))
            {
                errorMessage = "Unlock mode must be ReadOnly or Full.";
                return false;
            }

            var duration = leaseMinutes ?? _options.DefaultLeaseMinutes;
            if (duration <= 0)
            {
                errorMessage = "Lease duration must be greater than zero.";
                return false;
            }

            var now = _timeProvider.GetUtcNow();

            if (_otpConsumed || now >= _otpExpiresAtUtc)
            {
                errorMessage = "OTP is invalid or expired.";
                return false;
            }

            if (!FixedTimeOtpEquals(_otp, otp))
            {
                errorMessage = "OTP is invalid or expired.";
                return false;
            }

            _otpConsumed = true;
            _otp = string.Empty;
            _mode = mode;
            _startedAtUtc = now;
            _leaseExpiresAtUtc = now.AddMinutes(duration);

            errorMessage = null;
            return true;
        }
    }

    public SessionStatus Lock()
    {
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            TransitionToLocked(now, generateFreshOtp: true);
            return CreateStatus();
        }
    }

    private void RefreshExpiredLease()
    {
        if (_mode == AgentAccessMode.Locked ||
            _leaseExpiresAtUtc is null ||
            _timeProvider.GetUtcNow() < _leaseExpiresAtUtc.Value)
        {
            return;
        }

        TransitionToLocked(
            _timeProvider.GetUtcNow(),
            generateFreshOtp: true);
    }

    private void TransitionToLocked(
        DateTimeOffset now,
        bool generateFreshOtp)
    {
        _mode = AgentAccessMode.Locked;
        _startedAtUtc = now;
        _leaseExpiresAtUtc = null;
        _otpConsumed = true;
        _otp = string.Empty;

        if (generateFreshOtp)
        {
            GenerateFreshOtp(now);
        }
    }

    private void GenerateFreshOtp(DateTimeOffset now)
    {
        _otp = _otpGenerator.Generate(_options.OtpDigits);
        _otpConsumed = false;
        _otpExpiresAtUtc = now.AddMinutes(_options.OtpLifetimeMinutes);
        WriteOtpToLocalStandardError(_otp, _options.OtpLifetimeMinutes);
    }

    private SessionStatus CreateStatus()
    {
        var otpActive =
            !_otpConsumed &&
            _timeProvider.GetUtcNow() < _otpExpiresAtUtc;

        return new SessionStatus(
            _mode,
            _startedAtUtc,
            _leaseExpiresAtUtc,
            otpActive,
            otpActive ? _otpExpiresAtUtc : null);
    }

    private static bool FixedTimeOtpEquals(string expected, string supplied)
    {
        if (string.IsNullOrEmpty(expected) ||
            string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);

        var equalLength = expectedBytes.Length == suppliedBytes.Length;
        var paddedSupplied = new byte[expectedBytes.Length];

        if (suppliedBytes.Length > 0)
        {
            Buffer.BlockCopy(
                suppliedBytes,
                0,
                paddedSupplied,
                0,
                Math.Min(suppliedBytes.Length, paddedSupplied.Length));
        }

        return equalLength &&
            CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                paddedSupplied);
    }

    private static void WriteOtpToLocalStandardError(
        string otp,
        int lifetimeMinutes)
    {
        Console.Error.WriteLine("╔══════════════════════════════════════╗");
        Console.Error.WriteLine("║       CODICKS_LITE MCP SESSION LOCKED     ║");
        Console.Error.WriteLine("║                                      ║");
        Console.Error.WriteLine($"║  Local OTP: {otp,-25}║");
        Console.Error.WriteLine($"║  OTP expires in: {lifetimeMinutes} minute(s)       ║");
        Console.Error.WriteLine("║                                      ║");
        Console.Error.WriteLine("║  Use codicks-lite-control locally to      ║");
        Console.Error.WriteLine("║  authorize READ_ONLY or FULL.        ║");
        Console.Error.WriteLine("╚══════════════════════════════════════╝");
    }
}
