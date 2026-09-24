using LocalAgent.Core.Configuration;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Execution;

public sealed class ExecutablePolicy : IExecutablePolicy
{
    private readonly ExecutionOptions _options;
    private readonly IWorkspacePermissionEvaluator _permissionEvaluator;

    public ExecutablePolicy(
        AgentConfiguration configuration,
        IWorkspacePermissionEvaluator permissionEvaluator)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(permissionEvaluator);

        _options = configuration.Execution;
        _permissionEvaluator = permissionEvaluator;
    }

    public ExecutionPolicyDecision Evaluate(
        WorkspaceDescriptor workspace,
        ProcessExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return Deny(
                ProcessExecutionError.InvalidRequest,
                "workspaceId is required.");
        }

        if (!string.Equals(
                workspace.Id,
                request.WorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Deny(
                ProcessExecutionError.InvalidRequest,
                "workspaceId does not match the resolved workspace.");
        }

        if (string.IsNullOrWhiteSpace(request.Executable))
        {
            return Deny(
                ProcessExecutionError.InvalidRequest,
                "executable is required.");
        }

        if (!_options.Enabled)
        {
            return Deny(
                ProcessExecutionError.ExecutionDisabled,
                "Local command execution is disabled.");
        }

        var workspacePermission =
            _permissionEvaluator.Evaluate(
                workspace,
                WorkspaceOperation.Execute);

        if (!workspacePermission.Allowed)
        {
            return Deny(
                ProcessExecutionError.WorkspaceExecutionDenied,
                workspacePermission.Message);
        }

        if (request.ExecutionMode == ExecutionMode.Sandbox &&
            !_options.Sandbox.Enabled)
        {
            return Deny(
                ProcessExecutionError.SandboxDisabled,
                "Sandbox execution is disabled.");
        }

        if (request.ExecutionMode is not
            (ExecutionMode.Host or ExecutionMode.Sandbox))
        {
            return Deny(
                ProcessExecutionError.UnsupportedExecutionMode,
                $"Execution mode '{request.ExecutionMode}' is not supported.");
        }

        if (request.Executable.Contains('/') ||
            request.Executable.Contains('\\'))
        {
            return Deny(
                ProcessExecutionError.ExecutableNotAllowed,
                "Executable must be a configured command name, not a path.");
        }

        if (!TryGetExecutable(
                request.Executable,
                out var executableOptions) ||
            !executableOptions.Enabled)
        {
            return Deny(
                ProcessExecutionError.ExecutableNotAllowed,
                $"Executable '{request.Executable}' is not enabled.");
        }

        if (!IsValidRelativeWorkingDirectory(
                request.RelativeWorkingDirectory))
        {
            return Deny(
                ProcessExecutionError.InvalidWorkingDirectory,
                "relativeWorkingDirectory must remain a traversal-free relative path.");
        }

        if (request.Arguments is null)
        {
            return Deny(
                ProcessExecutionError.InvalidRequest,
                "arguments is required.");
        }

        var command = request.Arguments.Count == 0
            ? null
            : request.Arguments[0];

        if (!string.IsNullOrWhiteSpace(command) &&
            ContainsCommand(
                executableOptions.DeniedCommands,
                command))
        {
            return Deny(
                ProcessExecutionError.CommandNotAllowed,
                $"Command '{command}' is denied for executable '{request.Executable}'.");
        }

        if (!executableOptions.AllowAnyArguments)
        {
            if (string.IsNullOrWhiteSpace(command) ||
                !ContainsCommand(
                    executableOptions.AllowedCommands,
                    command))
            {
                return Deny(
                    ProcessExecutionError.CommandNotAllowed,
                    $"Command '{command ?? string.Empty}' is not allowed for executable '{request.Executable}'.");
            }
        }

        var maxTimeout =
            executableOptions.MaxTimeoutSeconds is int executableMax
                ? Math.Min(
                    _options.MaxTimeoutSeconds,
                    executableMax)
                : _options.MaxTimeoutSeconds;

        var effectiveTimeout =
            request.TimeoutSeconds ?? maxTimeout;

        if (effectiveTimeout <= 0 ||
            effectiveTimeout > maxTimeout)
        {
            return Deny(
                ProcessExecutionError.TimeoutOutOfRange,
                $"timeoutSeconds must be between 1 and {maxTimeout}.");
        }

        var effectiveOutputLimit =
            request.MaxOutputBytes ?? _options.MaxOutputBytes;

        if (effectiveOutputLimit <= 0 ||
            effectiveOutputLimit > _options.MaxOutputBytes)
        {
            return Deny(
                ProcessExecutionError.OutputLimitOutOfRange,
                $"maxOutputBytes must be between 1 and {_options.MaxOutputBytes}.");
        }

        return ExecutionPolicyDecision.Allow(
            effectiveTimeout,
            effectiveOutputLimit);
    }

    private bool TryGetExecutable(
        string executable,
        out ExecutableExecutionOptions options)
    {
        foreach (var pair in _options.Executables)
        {
            if (string.Equals(
                    pair.Key,
                    executable,
                    StringComparison.OrdinalIgnoreCase))
            {
                options = pair.Value;
                return true;
            }
        }

        options = null!;
        return false;
    }

    private static bool ContainsCommand(
        List<string> commands,
        string command) =>
        commands.Contains(
            command,
            StringComparer.OrdinalIgnoreCase);

    private static bool IsValidRelativeWorkingDirectory(
        string relativeWorkingDirectory)
    {
        if (string.IsNullOrEmpty(relativeWorkingDirectory))
        {
            return true;
        }

        if (Path.IsPathRooted(relativeWorkingDirectory) ||
            relativeWorkingDirectory.Contains('\0'))
        {
            return false;
        }

        var normalized =
            relativeWorkingDirectory.Replace(
                '\\',
                '/');

        return normalized
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .All(segment =>
                !string.Equals(
                    segment,
                    "..",
                    StringComparison.Ordinal));
    }

    private static ExecutionPolicyDecision Deny(
        ProcessExecutionError error,
        string message) =>
        ExecutionPolicyDecision.Deny(
            error,
            message);
}
