using System.Security.Cryptography;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.Security;

public sealed class SessionOtpGenerator : ISessionOtpGenerator
{
    public string Generate(int digits)
    {
        if (digits < 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(digits),
                "OTP digit count must be at least 6.");
        }

        return string.Create(
            digits,
            state: 0,
            static (span, _) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));
                }
            });
    }
}
