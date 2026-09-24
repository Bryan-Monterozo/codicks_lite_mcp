using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class ConfigurationMcpIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("cfg");

    [Fact]
    public async Task ServerInfo_ReflectsExternalConfigurationAfterRestart()
    {
        var workspaceRoot = Path.Combine(_root, "workspace");
        var stateRoot = Path.Combine(_root, "state");
        var configDirectory = Path.Combine(_root, "config");
        var configPath = Path.Combine(configDirectory, "agent.json");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(configDirectory);

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "integration-mac",
                  "DisplayName": "Integration Mac"
                },
                "ReadOnly": true,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Workspaces": {
                  "integration": {
                    "Root": "{{JsonEscape(workspaceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": ["read"]
                  }
                }
              }
            }
            """);

        var hostAssembly = typeof(HostMarker).Assembly.Location;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "codicks-lite-chunk02-config-integration",
            Command = "dotnet",
            Arguments = [hostAssembly],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["CODICKS_LITE_CONFIG_FILE"] = configPath
            }
        });

        await using var client = await McpClient.CreateAsync(transport);
        var result = await client.CallToolAsync(
            "server_info",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsError is true);

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("integration-mac", text!, StringComparison.Ordinal);
        Assert.Contains("Integration Mac", text, StringComparison.Ordinal);
        Assert.Contains("workspaceCount", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("true", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
