using System.Security.Cryptography;
using LocalAgent.Core.Files;

namespace LocalAgent.Infrastructure.Files;

public sealed class MemoryFilePatchReviewStore(
    TimeProvider timeProvider) : IFilePatchReviewStore
{
    private const int MaxEntries = 256;
    private static readonly TimeSpan Lifetime =
        TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly Dictionary<string, FilePatchReviewReceipt> _receipts =
        new(StringComparer.Ordinal);

    public FilePatchReviewReceipt Issue(
        FilePatchReviewBinding binding,
        string patch)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(patch);

        lock (_sync)
        {
            if (_receipts.Count >= MaxEntries)
            {
                var oldest = _receipts.Values
                    .OrderBy(receipt => receipt.IssuedAtUtc)
                    .ThenBy(receipt => receipt.Token, StringComparer.Ordinal)
                    .First();

                _receipts.Remove(oldest.Token);
            }

            var now = timeProvider.GetUtcNow();
            string token;

            do
            {
                token = CreateToken();
            }
            while (_receipts.ContainsKey(token));

            var receipt = new FilePatchReviewReceipt(
                token,
                binding,
                patch,
                now,
                now.Add(Lifetime),
                Consumed: false);

            _receipts[token] = receipt;
            return receipt;
        }
    }

    public FilePatchReviewValidation Validate(
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new FilePatchReviewValidation(
                false,
                null,
                FilePatchReviewValidationError.ReviewRequired);
        }

        lock (_sync)
        {
            if (!_receipts.TryGetValue(
                    token,
                    out var receipt))
            {
                return new FilePatchReviewValidation(
                    false,
                    null,
                    FilePatchReviewValidationError.InvalidToken);
            }

            if (receipt.Consumed)
            {
                return new FilePatchReviewValidation(
                    false,
                    receipt,
                    FilePatchReviewValidationError.ConsumedToken);
            }

            if (timeProvider.GetUtcNow() >
                receipt.ExpiresAtUtc)
            {
                var expired =
                    receipt with
                    {
                        Patch = string.Empty
                    };

                _receipts[token] =
                    expired;

                return new FilePatchReviewValidation(
                    false,
                    expired,
                    FilePatchReviewValidationError.ExpiredToken);
            }

            return new FilePatchReviewValidation(
                true,
                receipt,
                FilePatchReviewValidationError.None);
        }
    }

    public bool TryConsume(
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_receipts.TryGetValue(
                    token,
                    out var receipt) ||
                receipt.Consumed)
            {
                return false;
            }

            _receipts[token] =
                receipt with
                {
                    Patch = string.Empty,
                    Consumed = true
                };

            return true;
        }
    }

    private static string CreateToken()
    {
        var bytes =
            RandomNumberGenerator.GetBytes(32);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
