using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class DeveloperWorkflowAcceptanceIntegrationTests : IDisposable
{
    private const string ProjectFile = "WorkflowFixture.csproj";
    private const string SourceFile = "Program.cs";

    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("wf");

    [Fact]
    public async Task EditBuildFixTestAndGitStatus_CompletesThroughMcp()
    {
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

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(configDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                ProjectFile),
            """
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />

              <Target Name="VSTest" DependsOnTargets="Build">
                <Message
                  Text="WORKFLOW_TEST_PASSED"
                  Importance="high" />
              </Target>
            </Project>
            """);

        const string brokenSource =
            "Console.WriteLine(\"broken\"";

        const string fixedSource =
            "Console.WriteLine(\"workflow-ok\");";

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                SourceFile),
            brokenSource);

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "developer-workflow-acceptance",
                  "DisplayName": "Developer Workflow Acceptance"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Workspaces": {
                  "workflow": {
                    "Root": "{{JsonEscape(workspaceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read",
                      "update",
                      "execute"
                    ]
                  }
                }
              },
              "Execution": {
                "Enabled": true,
                "MaxTimeoutSeconds": 60,
                "MaxOutputBytes": 65536,
                "Executables": {
                  "dotnet": {
                    "Enabled": true,
                    "AllowAnyArguments": true
                  },
                  "git": {
                    "Enabled": true,
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
                        "codicks-developer-workflow-acceptance",
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

        var gitInit =
            await CallProcessExecAsync(
                client,
                "git",
                ["init"]);

        AssertProcessExitCode(
            gitInit,
            expectedExitCode: 0,
            "git init");

        var restore =
            await CallProcessExecAsync(
                client,
                "dotnet",
                ["restore", ProjectFile]);

        AssertProcessExitCode(
            restore,
            expectedExitCode: 0,
            "dotnet restore");

        var failedBuild =
            await CallProcessExecAsync(
                client,
                "dotnet",
                [
                    "build",
                    ProjectFile,
                    "--no-restore"
                ]);

        AssertToolSuccess(
            failedBuild,
            "initial failing build");

        Assert.NotEqual(
            0,
            GetRequiredInt32(
                failedBuild.StructuredContent!.Value,
                "exitCode"));

        var failedBuildOutput =
            GetProcessOutput(
                failedBuild);

        Assert.Contains(
            "error CS",
            failedBuildOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            SourceFile,
            failedBuildOutput,
            StringComparison.Ordinal);

        var read =
            await client.CallToolAsync(
                "file_read",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "workflow",
                    ["relativePath"] = SourceFile,
                    ["byteOffset"] = 0
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            read,
            "file_read");

        Assert.Equal(
            brokenSource,
            GetRequiredString(
                read.StructuredContent!.Value,
                "content"));

        var update =
            await client.CallToolAsync(
                "file_update",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "workflow",
                    ["relativePath"] = SourceFile,
                    ["content"] = fixedSource,
                    ["expectedHash"] =
                        GetRequiredString(
                            read.StructuredContent!.Value,
                            "sha256"),
                    ["dryRun"] = false
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            update,
            "file_update");

        var successfulBuild =
            await CallProcessExecAsync(
                client,
                "dotnet",
                [
                    "build",
                    ProjectFile,
                    "--no-restore"
                ]);

        AssertProcessExitCode(
            successfulBuild,
            expectedExitCode: 0,
            "fixed dotnet build");

        var test =
            await CallProcessExecAsync(
                client,
                "dotnet",
                [
                    "test",
                    ProjectFile,
                    "--no-restore"
                ]);

        AssertProcessExitCode(
            test,
            expectedExitCode: 0,
            "dotnet test");

        Assert.Contains(
            "WORKFLOW_TEST_PASSED",
            GetProcessOutput(test),
            StringComparison.Ordinal);

        var gitStatus =
            await CallProcessExecAsync(
                client,
                "git",
                ["status", "--short"]);

        AssertProcessExitCode(
            gitStatus,
            expectedExitCode: 0,
            "git status");

        var gitStatusOutput =
            GetRequiredString(
                gitStatus.StructuredContent!.Value,
                "standardOutput");

        Assert.Contains(
            SourceFile,
            gitStatusOutput,
            StringComparison.Ordinal);

        Assert.Contains(
            ProjectFile,
            gitStatusOutput,
            StringComparison.Ordinal);

        Assert.Equal(
            fixedSource,
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspaceRoot,
                    SourceFile)));
    }

    private static ValueTask<CallToolResult> CallProcessExecAsync(
        McpClient client,
        string executable,
        string[] arguments) =>
        client.CallToolAsync(
            "process_exec",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "workflow",
                ["executable"] = executable,
                ["arguments"] = arguments,
                ["relativeWorkingDirectory"] = "",
                ["executionMode"] = "Host"
            },
            cancellationToken:
                CancellationToken.None);

    private static void AssertProcessExitCode(
        CallToolResult result,
        int expectedExitCode,
        string operation)
    {
        AssertToolSuccess(
            result,
            operation);

        Assert.Equal(
            expectedExitCode,
            GetRequiredInt32(
                result.StructuredContent!.Value,
                "exitCode"));
    }

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

    private static string GetProcessOutput(
        CallToolResult result)
    {
        var structuredContent =
            result.StructuredContent!.Value;

        return string.Concat(
            GetRequiredString(
                structuredContent,
                "standardOutput"),
            "\n",
            GetRequiredString(
                structuredContent,
                "standardError"));
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
        var property =
            GetRequiredProperty(
                structuredContent,
                propertyName);

        var value = property.GetString();

        Assert.NotNull(value);

        return value;
    }

    private static int GetRequiredInt32(
        JsonElement structuredContent,
        string propertyName) =>
        GetRequiredProperty(
            structuredContent,
            propertyName)
            .GetInt32();

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
}
