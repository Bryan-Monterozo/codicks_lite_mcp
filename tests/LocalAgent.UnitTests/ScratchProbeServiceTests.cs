using LocalAgent.Infrastructure.Scratch;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class ScratchProbeServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk01-unit-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteProbeAsync_WritesOnlyFixedProbeFile()
    {
        var service = new ScratchProbeService(_tempDirectory);

        var result = await service.WriteProbeAsync("hello from unit test");

        Assert.True(result.Success);
        Assert.Equal(ScratchProbeService.ProbeFileName, result.FileName);
        Assert.Equal("hello from unit test", await File.ReadAllTextAsync(result.FullPath));
        Assert.Single(Directory.GetFiles(_tempDirectory));
    }

    [Fact]
    public async Task WriteProbeAsync_RejectsOversizedContent()
    {
        var service = new ScratchProbeService(_tempDirectory);
        var content = new string('x', ScratchProbeService.MaxContentLength + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.WriteProbeAsync(content));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
