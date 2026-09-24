using LocalAgent.Host;
using LocalAgent.Infrastructure.Scratch;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class StdioMcpIntegrationTests : IDisposable
{
    private readonly string _scratchDirectory =
        TestSessionUnlocker.CreateShortRoot("c01");

    [Fact]
    public async Task Host_AdvertisesTools_AndExecutesScratchProbe()
    {
        Directory.CreateDirectory(_scratchDirectory);
        var hostAssembly = typeof(HostMarker).Assembly.Location;
        var isolatedConfigPath = Path.Combine(_scratchDirectory, "missing-agent.json");
        var stateRoot = Path.Combine(_scratchDirectory, "state");
        var sessionUnlocker = new TestSessionUnlocker();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "codicks-lite-chunk01-integration",
            Command = "dotnet",
            Arguments = [hostAssembly],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [ScratchProbeService.ScratchDirectoryEnvironmentVariable] = _scratchDirectory,
                ["CODICKS_LITE_CONFIG_FILE"] = isolatedConfigPath,
                ["CODICKS_LITE_Agent__StateDirectory"] = stateRoot
            },
            StandardErrorLines = sessionUnlocker.StandardErrorLines
        });

        await using var client = await McpClient.CreateAsync(transport);
        await sessionUnlocker.UnlockFullAsync(stateRoot);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "server_info");
        Assert.Contains(tools, tool => tool.Name == "scratch_write_probe");
        Assert.Contains(tools, tool => tool.Name == "scratch_read_probe");

        var result = await client.CallToolAsync(
            "scratch_write_probe",
            new Dictionary<string, object?>
            {
                ["content"] = "hello through MCP stdio"
            },
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsError is true);
        Assert.True(File.Exists(Path.Combine(_scratchDirectory, ScratchProbeService.ProbeFileName)));
        Assert.Equal(
            "hello through MCP stdio",
            await File.ReadAllTextAsync(Path.Combine(_scratchDirectory, ScratchProbeService.ProbeFileName)));

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDirectory))
        {
            Directory.Delete(_scratchDirectory, recursive: true);
        }
    }
}
