using LocalAgent.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAgent.Host;

public static class LocalSessionCommand
{
    private const string Flag =
        "--local-session";

    public static async Task<bool> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            args);

        ArgumentNullException.ThrowIfNull(
            services);

        if (args.Length == 0 ||
            !string.Equals(
                args[0],
                Flag,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (args.Length < 2)
        {
            Fail(
                "Usage: --local-session <status|lock|read|full> [--otp <OTP>] [--for <minutes>]",
                exitCode: 2);

            return true;
        }

        LocalSessionControlRequest request;

        try
        {
            request =
                ParseRequest(
                    args[1..]);
        }
        catch (ArgumentException exception)
        {
            Fail(
                exception.Message,
                exitCode: 2);

            return true;
        }

        try
        {
            var client =
                services.GetRequiredService<
                    ILocalSessionControlClient>();

            var response =
                await client.SendAsync(
                    request,
                    cancellationToken);

            if (!response.Success)
            {
                Fail(
                    response.Error ??
                    "Local session-control request failed.",
                    exitCode: 1);

                return true;
            }

            if (response.Status is null)
            {
                Fail(
                    "Local session-control response did not include status.",
                    exitCode: 1);

                return true;
            }

            PrintStatus(
                response.Status);

            return true;
        }
        catch (Exception exception)
            when (exception is
                IOException or
                TimeoutException or
                PlatformNotSupportedException or
                OperationCanceledException)
        {
            Fail(
                $"Local session control failed: {exception.Message}",
                exitCode: 1);

            return true;
        }
    }

    private static LocalSessionControlRequest ParseRequest(
        string[] args)
    {
        var action =
            args[0]
                .Trim()
                .ToLowerInvariant();

        if (action ==
            "status")
        {
            RequireNoArguments(
                args);

            return new LocalSessionControlRequest(
                "status");
        }

        if (action ==
            "lock")
        {
            RequireNoArguments(
                args);

            return new LocalSessionControlRequest(
                "lock");
        }

        if (action is not
            ("read" or "full"))
        {
            throw new ArgumentException(
                "Session action must be status, lock, read, or full.");
        }

        string? otp =
            null;

        int? leaseMinutes =
            null;

        for (var index = 1;
             index < args.Length;
             index++)
        {
            switch (args[index])
            {
                case "--otp":
                    if (index + 1 >=
                        args.Length)
                    {
                        throw new ArgumentException(
                            "--otp requires a value.");
                    }

                    otp =
                        args[++index];

                    break;

                case "--for":
                    if (index + 1 >=
                        args.Length ||
                        !int.TryParse(
                            args[++index],
                            out var parsedLease) ||
                        parsedLease <= 0)
                    {
                        throw new ArgumentException(
                            "--for requires a positive integer number of minutes.");
                    }

                    leaseMinutes =
                        parsedLease;

                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown session argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(
                otp) ||
            !otp.All(
                char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "--otp is required and must contain digits only.");
        }

        return new LocalSessionControlRequest(
            "unlock",
            action ==
                "read"
                ? "ReadOnly"
                : "Full",
            otp,
            leaseMinutes);
    }

    private static void RequireNoArguments(
        string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException(
                $"Session action '{args[0]}' does not accept additional arguments.");
        }
    }

    private static void PrintStatus(
        SessionStatus status)
    {
        Console.WriteLine(
            "Codicks Lite MCP Session");

        Console.WriteLine();

        Console.WriteLine(
            $"Mode: {status.Mode.ToString().ToUpperInvariant()}");

        Console.WriteLine(
            $"Started: {status.StartedAtUtc:O}");

        Console.WriteLine(
            $"Lease expires: {FormatDate(status.LeaseExpiresAtUtc)}");

        Console.WriteLine(
            $"OTP state: {(status.OtpActive ? "active" : "consumed/expired")}");

        Console.WriteLine(
            $"OTP expires: {FormatDate(status.OtpExpiresAtUtc)}");
    }

    private static string FormatDate(
        DateTimeOffset? value) =>
        value?.ToString(
            "O") ??
        "none";

    private static void Fail(
        string message,
        int exitCode)
    {
        Console.Error.WriteLine(
            $"ERROR: {message}");

        Environment.ExitCode =
            exitCode;
    }
}
