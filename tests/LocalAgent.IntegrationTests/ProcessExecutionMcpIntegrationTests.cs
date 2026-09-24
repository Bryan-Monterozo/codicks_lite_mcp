using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class ProcessExecutionMcpIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("exec");

    [Fact]
    public async Task ProcessExec_FullSession_ExecutesAndEnforcesPolicy()
    {
        var fixture = await CreateHostFixtureAsync(
            "full");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var tools = await client.ListToolsAsync();

        Assert.Contains(
            tools,
            tool => tool.Name == "process_exec");

        var missingRequiredArgument =
            await client.CallToolAsync(
                "process_exec",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "exec"
                },
                cancellationToken: CancellationToken.None);

        Assert.True(
            missingRequiredArgument.IsError is true);

        var success = await CallProcessExecAsync(
            client,
            "exec",
            "dotnet",
            ["--version"]);

        AssertToolSuccess(
            success,
            "dotnet --version");

        Assert.Equal(
            0,
            GetRequiredInt32(
                success.StructuredContent!.Value,
                "exitCode"));

        Assert.False(
            string.IsNullOrWhiteSpace(
                GetRequiredString(
                    success.StructuredContent!.Value,
                    "standardOutput")));

        var nonZero = await CallProcessExecAsync(
            client,
            "exec",
            "git",
            ["definitely-not-a-command"]);

        AssertToolSuccess(
            nonZero,
            "git invalid command");

        Assert.NotEqual(
            0,
            GetRequiredInt32(
                nonZero.StructuredContent!.Value,
                "exitCode"));

        var largeText = new string(
            'x',
            512);

        var truncated = await CallProcessExecAsync(
            client,
            "exec",
            "printf",
            [largeText],
            maxOutputBytes: 32);

        AssertToolSuccess(
            truncated,
            "printf truncation");

        Assert.True(
            GetRequiredBoolean(
                truncated.StructuredContent!.Value,
                "standardOutputTruncated"));

        var retainedOutput =
            GetRequiredString(
                truncated.StructuredContent!.Value,
                "standardOutput");

        Assert.True(
            Encoding.UTF8.GetByteCount(
                retainedOutput) <= 32);

        var timeout = await CallProcessExecAsync(
            client,
            "exec",
            "sleep",
            ["2"],
            timeoutSeconds: 1);

        AssertToolSuccess(
            timeout,
            "sleep timeout");

        Assert.True(
            GetRequiredBoolean(
                timeout.StructuredContent!.Value,
                "timedOut"));

        var deniedExecutable = await CallProcessExecAsync(
            client,
            "exec",
            "node",
            ["--version"]);

        AssertToolErrorContains(
            deniedExecutable,
            "EXECUTABLE_NOT_ALLOWED");

        var disabledExecutable = await CallProcessExecAsync(
            client,
            "exec",
            "disabled-tool",
            ["anything"]);

        AssertToolErrorContains(
            disabledExecutable,
            "EXECUTABLE_NOT_ALLOWED");

        var deniedCommand = await CallProcessExecAsync(
            client,
            "exec",
            "git",
            ["push"]);

        AssertToolErrorContains(
            deniedCommand,
            "COMMAND_NOT_ALLOWED");

        var noExecutePermission = await CallProcessExecAsync(
            client,
            "noexec",
            "dotnet",
            ["--version"]);

        AssertToolErrorContains(
            noExecutePermission,
            "WORKSPACE_EXECUTION_DENIED");

        var invalidWorkspace = await CallProcessExecAsync(
            client,
            "missing",
            "dotnet",
            ["--version"]);

        AssertToolErrorContains(
            invalidWorkspace,
            "WORKSPACE_NOT_FOUND");

        var traversal = await CallProcessExecAsync(
            client,
            "exec",
            "dotnet",
            ["--version"],
            relativeWorkingDirectory: "../outside");

        AssertToolErrorContains(
            traversal,
            "INVALID_WORKING_DIRECTORY");
    }

    [Fact]
    public async Task ProcessExec_Audit_IsMetadataOnly_AndRecordsFailures()
    {
        const string secretArgument =
            "SYNTHETIC_SECRET_ARGUMENT_94c71a";
        const string secretEnvironmentValue =
            "SYNTHETIC_ENV_SECRET_VALUE_07b3";

        var fixture = await CreateHostFixtureAsync(
            "audit",
            secretEnvironmentValue);

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var secretOutput = await CallProcessExecAsync(
            client,
            "exec",
            "printf",
            [secretArgument]);

        AssertToolSuccess(
            secretOutput,
            "printf secret output");

        var environment = await CallProcessExecAsync(
            client,
            "exec",
            "env",
            []);

        AssertToolSuccess(
            environment,
            "env filtering");

        Assert.DoesNotContain(
            secretEnvironmentValue,
            GetRequiredString(
                environment.StructuredContent!.Value,
                "standardOutput"),
            StringComparison.Ordinal);

        var truncated = await CallProcessExecAsync(
            client,
            "exec",
            "printf",
            [new string('z', 256)],
            maxOutputBytes: 16);

        AssertToolSuccess(
            truncated,
            "audit truncation");

        var timeout = await CallProcessExecAsync(
            client,
            "exec",
            "sleep",
            ["2"],
            timeoutSeconds: 1);

        AssertToolSuccess(
            timeout,
            "audit timeout");

        var denied = await CallProcessExecAsync(
            client,
            "exec",
            "node",
            ["--version"]);

        AssertToolErrorContains(
            denied,
            "EXECUTABLE_NOT_ALLOWED");

        var traversal = await CallProcessExecAsync(
            client,
            "exec",
            "dotnet",
            ["--version"],
            relativeWorkingDirectory: "../outside");

        AssertToolErrorContains(
            traversal,
            "INVALID_WORKING_DIRECTORY");

        var startFailure = await CallProcessExecAsync(
            client,
            "exec",
            "missing-tool",
            ["anything"]);

        AssertToolErrorContains(
            startFailure,
            "PROCESS_START_FAILED");

        var auditPath = Path.Combine(
            fixture.StateRoot,
            "audit",
            "operations.jsonl");

        Assert.True(
            File.Exists(auditPath));

        var lines =
            await File.ReadAllLinesAsync(
                auditPath);

        Assert.NotEmpty(lines);

        var auditText =
            string.Join(
                "\n",
                lines);

        Assert.DoesNotContain(
            secretArgument,
            auditText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secretEnvironmentValue,
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "EXECUTION_TIMEOUT",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "EXECUTABLE_NOT_ALLOWED",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "INVALID_WORKING_DIRECTORY",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "PROCESS_START_FAILED",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"StandardOutputTruncated\":true",
            auditText,
            StringComparison.Ordinal);

        foreach (var line in lines)
        {
            using var json =
                JsonDocument.Parse(line);

            if (!string.Equals(
                    json.RootElement
                        .GetProperty("Operation")
                        .GetString(),
                    "process_exec",
                    StringComparison.Ordinal))
            {
                continue;
            }

            Assert.False(
                json.RootElement.TryGetProperty(
                    "Arguments",
                    out _));

            Assert.False(
                json.RootElement.TryGetProperty(
                    "StandardOutput",
                    out _));

            Assert.False(
                json.RootElement.TryGetProperty(
                    "StandardError",
                    out _));

            Assert.False(
                json.RootElement.TryGetProperty(
                    "Environment",
                    out _));
        }
    }

    [Fact]
    public async Task ProcessExec_LockedSession_IsRejected()
    {
        var fixture = await CreateHostFixtureAsync(
            "locked");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        var result = await CallProcessExecAsync(
            client,
            "exec",
            "dotnet",
            ["--version"]);

        AssertToolErrorContains(
            result,
            "SESSION_LOCKED");

        await AssertAuditContainsAsync(
            fixture.StateRoot,
            "SESSION_LOCKED");
    }

    [Fact]
    public async Task ProcessExec_ReadOnlySession_IsRejected()
    {
        var fixture = await CreateHostFixtureAsync(
            "readonly");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockReadOnlyAsync(
            fixture.StateRoot);

        var result = await CallProcessExecAsync(
            client,
            "exec",
            "dotnet",
            ["--version"]);

        AssertToolErrorContains(
            result,
            "SESSION_READ_ONLY");

        await AssertAuditContainsAsync(
            fixture.StateRoot,
            "SESSION_READ_ONLY");
    }

    private async Task<HostFixture> CreateHostFixtureAsync(
        string suffix,
        string? syntheticSecretEnvironmentValue = null)
    {
        var fixtureRoot = Path.Combine(
            _root,
            suffix);

        var workspaceRoot = Path.Combine(
            fixtureRoot,
            "workspace");

        var noExecRoot = Path.Combine(
            fixtureRoot,
            "noexec");

        var stateRoot = Path.Combine(
            fixtureRoot,
            "state");

        var configDirectory = Path.Combine(
            fixtureRoot,
            "config");

        var configPath = Path.Combine(
            configDirectory,
            "agent.json");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(noExecRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(configDirectory);

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "process-exec-{{suffix}}",
                  "DisplayName": "Process Exec {{suffix}}"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Workspaces": {
                  "exec": {
                    "Root": "{{JsonEscape(workspaceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read",
                      "execute"
                    ]
                  },
                  "noexec": {
                    "Root": "{{JsonEscape(noExecRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read"
                    ]
                  }
                }
              },
              "Execution": {
                "Enabled": true,
                "MaxTimeoutSeconds": 5,
                "MaxOutputBytes": 4096,
                "Executables": {
                  "dotnet": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  },
                  "git": {
                    "Enabled": true,
                    "AllowAnyArguments": true,
                    "DeniedCommands": [
                      "push"
                    ]
                  },
                  "printf": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  },
                  "sleep": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  },
                  "env": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  },
                  "missing-tool": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  },
                  "disabled-tool": {
                    "Enabled": false,
                    "AllowAnyArguments": true
                  }
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
                        $"codicks-process-exec-{suffix}",
                    Command = "dotnet",
                    Arguments = [hostAssembly],
                    EnvironmentVariables =
                        new Dictionary<string, string?>
                        {
                            ["CODICKS_LITE_CONFIG_FILE"] =
                                configPath,
                            ["CODICKS_TEST_SECRET_TOKEN"] =
                                syntheticSecretEnvironmentValue
                        },
                    StandardErrorLines =
                        sessionUnlocker.StandardErrorLines
                });

        return new HostFixture(
            stateRoot,
            sessionUnlocker,
            transport);
    }

    private static ValueTask<CallToolResult> CallProcessExecAsync(
        McpClient client,
        string workspaceId,
        string executable,
        string[] arguments,
        string relativeWorkingDirectory = "",
        int? timeoutSeconds = null,
        int? maxOutputBytes = null) =>
        client.CallToolAsync(
            "process_exec",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["executable"] = executable,
                ["arguments"] = arguments,
                ["relativeWorkingDirectory"] =
                    relativeWorkingDirectory,
                ["timeoutSeconds"] = timeoutSeconds,
                ["maxOutputBytes"] = maxOutputBytes,
                ["executionMode"] = "Host"
            },
            cancellationToken:
                CancellationToken.None);

    private static void AssertToolSuccess(
        CallToolResult result,
        string operation)
    {
        Assert.False(
            result.IsError is true,
            $"{operation} failed: {GetResultText(result)}");

        Assert.True(
            result.StructuredContent.HasValue,
            $"{operation} did not return structuredContent.");
    }

    private static void AssertToolErrorContains(
        CallToolResult result,
        string expectedCode)
    {
        Assert.True(
            result.IsError is true);

        Assert.Contains(
            expectedCode,
            GetResultText(result),
            StringComparison.Ordinal);
    }

    private static async Task AssertAuditContainsAsync(
        string stateRoot,
        string expectedText)
    {
        var auditPath = Path.Combine(
            stateRoot,
            "audit",
            "operations.jsonl");

        Assert.True(
            File.Exists(auditPath));

        var auditText =
            await File.ReadAllTextAsync(
                auditPath);

        Assert.Contains(
            expectedText,
            auditText,
            StringComparison.Ordinal);
    }

    private static string GetResultText(
        CallToolResult result) =>
        string.Join(
            " | ",
            result.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text));

    private static string GetRequiredString(
        JsonElement structuredContent,
        string propertyName)
    {
        var value = GetRequiredProperty(
            structuredContent,
            propertyName);

        var text = value.GetString();

        Assert.NotNull(text);

        return text;
    }

    private static int GetRequiredInt32(
        JsonElement structuredContent,
        string propertyName) =>
        GetRequiredProperty(
            structuredContent,
            propertyName).GetInt32();

    private static bool GetRequiredBoolean(
        JsonElement structuredContent,
        string propertyName) =>
        GetRequiredProperty(
            structuredContent,
            propertyName).GetBoolean();

    private static JsonElement GetRequiredProperty(
        JsonElement structuredContent,
        string propertyName)
    {
        foreach (var property in
                 structuredContent.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

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

    private sealed record HostFixture(
        string StateRoot,
        TestSessionUnlocker SessionUnlocker,
        StdioClientTransport Transport);
}
