using LocalAgent.Core.Files;
using LocalAgent.Infrastructure.Files;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class MemoryFilePatchReviewStoreTests
{
    [Fact]
    public void IssueAndValidate_ReturnsBoundReceipt()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(
                2026,
                9,
                24,
                9,
                0,
                0,
                TimeSpan.Zero));

        var store =
            new MemoryFilePatchReviewStore(
                clock);

        var binding =
            CreateBinding("one");

        var receipt =
            store.Issue(binding, "patch-one");

        Assert.Equal(
            43,
            receipt.Token.Length);

        Assert.Equal(
            binding,
            receipt.Binding);

        Assert.Equal(
            "patch-one",
            receipt.Patch);

        Assert.Equal(
            clock.GetUtcNow().AddMinutes(10),
            receipt.ExpiresAtUtc);

        var validation =
            store.Validate(
                receipt.Token);

        Assert.True(validation.Valid);
        Assert.Equal(
            receipt,
            validation.Receipt);
    }

    [Fact]
    public void MissingUnknownExpiredAndConsumed_AreDistinct()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(
                2026,
                9,
                24,
                9,
                0,
                0,
                TimeSpan.Zero));

        var store =
            new MemoryFilePatchReviewStore(
                clock);

        Assert.Equal(
            FilePatchReviewValidationError.ReviewRequired,
            store.Validate(string.Empty).Error);

        Assert.Equal(
            FilePatchReviewValidationError.InvalidToken,
            store.Validate("unknown").Error);

        var expired =
            store.Issue(
                CreateBinding("expired"),
                "patch-expired");

        clock.Advance(
            TimeSpan.FromMinutes(11));

        Assert.Equal(
            FilePatchReviewValidationError.ExpiredToken,
            store.Validate(expired.Token).Error);

        var current =
            store.Issue(
                CreateBinding("current"),
                "patch-current");

        Assert.True(
            store.TryConsume(
                current.Token));

        Assert.Equal(
            FilePatchReviewValidationError.ConsumedToken,
            store.Validate(current.Token).Error);
    }

    [Fact]
    public void NewStore_DoesNotKnowTokensFromPriorStore()
    {
        var clock = new ManualTimeProvider(
            DateTimeOffset.UtcNow);

        var first =
            new MemoryFilePatchReviewStore(
                clock);

        var token =
            first.Issue(
                CreateBinding("restart"),
                "patch-restart")
                .Token;

        var restarted =
            new MemoryFilePatchReviewStore(
                clock);

        Assert.Equal(
            FilePatchReviewValidationError.InvalidToken,
            restarted.Validate(token).Error);
    }

    [Fact]
    public void Store_IsBoundedToMostRecentReceipts()
    {
        var clock = new ManualTimeProvider(
            DateTimeOffset.UtcNow);

        var store =
            new MemoryFilePatchReviewStore(
                clock);

        string? firstToken = null;
        string? lastToken = null;

        for (var index = 0;
             index < 257;
             index++)
        {
            var receipt =
                store.Issue(
                    CreateBinding(
                        index.ToString()),
                    $"patch-{index}");

            firstToken ??=
                receipt.Token;

            lastToken =
                receipt.Token;

            clock.Advance(
                TimeSpan.FromSeconds(1));
        }

        Assert.NotNull(firstToken);
        Assert.NotNull(lastToken);

        Assert.Equal(
            FilePatchReviewValidationError.InvalidToken,
            store.Validate(firstToken).Error);

        Assert.True(
            store.Validate(lastToken).Valid);
    }

    private static FilePatchReviewBinding CreateBinding(
        string suffix) =>
        new(
            "workspace",
            $"file-{suffix}.txt",
            new string('A', 64),
            new string('B', 64),
            new string('C', 64),
            new string('D', 64));

    private sealed class ManualTimeProvider(
        DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current =
            current;

        public override DateTimeOffset GetUtcNow() =>
            _current;

        public void Advance(
            TimeSpan duration) =>
            _current =
                _current.Add(
                    duration);
    }
}
