using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class SandboxExecutionMcpIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("sbx");

    [Fact]
    public async Task ProcessExec_SandboxMode_RoutesThroughConfiguredBackend()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var workspaceRoot = Path.Combine(
            _root,
            "workspace");

        var stateRoot = Path.Combine(
            _root,
            "state");

        var configDirectory = Path.Combine(
            _root,
            "config");

        var configPath = Path.Combine(
            configDirectory,
            "agent.json");

        var capturePath = Path.Combine(
            _root,
            "sandbox-args.txt");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(configDirectory);

        var runtimePath =
            CreateFakeRuntime(
                capturePath);

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "sandbox-mcp-integration",
                  "DisplayName": "Sandbox MCP Integration"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Workspaces": {
                  "sandbox": {
                    "Root": "{{JsonEscape(workspaceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read",
                      "execute"
                    ]
                  }
                }
              },
              "Execution": {
                "Enabled": true,
                "MaxTimeoutSeconds": 30,
                "MaxOutputBytes": 4096,
                "Executables": {
                  "dotnet": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  }
                },
                "Sandbox": {
                  "Enabled": true,
                  "RuntimeExecutable": "{{JsonEscape(runtimePath)}}",
                  "Image": "fake-dotnet-image",
                  "CpuCount": 1,
                  "MemoryMegabytes": 512,
                  "TmpfsMegabytes": 32,
                  "ReadOnlyRoot": true,
                  "NetworkEnabled": false
                }
              }
            }
            """);

        var hostAssembly =
            typeof(HostMarker).Assembly.Location;

        var sessionUnlocker =
            new TestSessionUnlocker();

        var transport =
            new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name =
                        "codicks-sandbox-mcp-integration",
                    Command = "dotnet",
                    Arguments = [hostAssembly],
                    EnvironmentVariables =
                        new Dictionary<string, string?>
                        {
                            ["CODICKS_LITE_CONFIG_FILE"] =
                                configPath
                        },
                    StandardErrorLines =
                        sessionUnlocker.StandardErrorLines
                });

        await using var client =
            await McpClient.CreateAsync(
                transport);

        await sessionUnlocker.UnlockFullAsync(
            stateRoot);

        var result =
            await client.CallToolAsync(
                "process_exec",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "sandbox",
                    ["executable"] = "dotnet",
                    ["arguments"] =
                        new[]
                        {
                            "test",
                            "Example.slnx"
                        },
                    ["relativeWorkingDirectory"] = "",
                    ["executionMode"] = "Sandbox"
                },
                cancellationToken:
                    CancellationToken.None);

        Assert.False(
            result.IsError is true,
            GetResultText(result));

        Assert.True(
            result.StructuredContent.HasValue);

        Assert.Equal(
            0,
            GetRequiredProperty(
                result.StructuredContent.Value,
                "exitCode")
                .GetInt32());

        Assert.Equal(
            "sandbox-mcp-ok",
            GetRequiredProperty(
                result.StructuredContent.Value,
                "standardOutput")
                .GetString());

        var arguments =
            await File.ReadAllLinesAsync(
                capturePath);

        Assert.Contains("--read-only", arguments);
        Assert.Contains("--network", arguments);
        Assert.Contains("none", arguments);
        Assert.Contains("--no-dns", arguments);
        Assert.Contains(
            $"{workspaceRoot}:/workspace",
            arguments);
        Assert.Contains(
            "fake-dotnet-image",
            arguments);
        Assert.Contains("dotnet", arguments);
        Assert.Contains("Example.slnx", arguments);
    }

    private string CreateFakeRuntime(
        string capturePath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }

        var runtimePath = Path.Combine(
            _root,
            "fake-container");

        var escapedCapture =
            capturePath.Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);

        var script = string.Join(
            '\n',
            "#!/bin/sh",
            $"capture=\"{escapedCapture}\"",
            "printf '%s\\n' \"$@\" > \"$capture\"",
            "printf 'sandbox-mcp-ok'",
            "");

        File.WriteAllText(
            runtimePath,
            script);

        File.SetUnixFileMode(
            runtimePath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        return runtimePath;
    }

    private static JsonElement GetRequiredProperty(
        JsonElement content,
        string name)
    {
        foreach (var property in
                 content.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{name}'.");
    }

    private static string GetResultText(
        CallToolResult result) =>
        string.Join(
            " | ",
            result.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text));

    private static string JsonEscape(
        string value) =>
        value.Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }
}
