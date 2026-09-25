using LocalAgent.Infrastructure.Platform;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WindowsCredentialManagerSecretStoreTests
{
    [Fact]
    public void StoreReadDelete_RoundTripsPerUserCredential()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var service =
            $"CodicksLiteMcp.Tests.{Guid.NewGuid():N}";

        const string secret =
            "test-runtime-key";

        var store =
            new WindowsCredentialManagerSecretStore();

        try
        {
            Assert.False(
                store.Contains(
                    service));

            store.Store(
                service,
                secret);

            Assert.True(
                store.Contains(
                    service));

            Assert.Equal(
                secret,
                store.Read(
                    service));

            Assert.True(
                store.Delete(
                    service));

            Assert.Null(
                store.Read(
                    service));
        }
        finally
        {
            _ =
                store.Delete(
                    service);
        }
    }
}
