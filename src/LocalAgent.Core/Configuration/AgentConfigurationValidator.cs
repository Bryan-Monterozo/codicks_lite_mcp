using System.Text.RegularExpressions;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Core.Configuration;

public sealed class AgentConfigurationValidator(IUserPathResolver pathResolver)
{
    private static readonly Regex WorkspaceIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<string> Validate(AgentConfiguration configuration, string configFilePath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<string>();

        if (configuration.SchemaVersion != AgentConfiguration.CurrentSchemaVersion)
        {
            errors.Add(
                $"SchemaVersion must be {AgentConfiguration.CurrentSchemaVersion}; received {configuration.SchemaVersion}.");
        }

        ValidateSessionSecurity(configuration.SessionSecurity, errors);
        ValidateAgent(configuration.Agent, configFilePath, errors);

        return errors;
    }

    private static void ValidateSessionSecurity(
        SessionSecurityOptions options,
        List<string> errors)
    {
        if (options.DefaultMode != AgentAccessMode.Locked)
        {
            errors.Add("SessionSecurity.DefaultMode must remain Locked for Codicks Lite v1.1.");
        }

        AddPositiveError(
            options.DefaultLeaseMinutes,
            "SessionSecurity.DefaultLeaseMinutes",
            errors);

        if (options.OtpDigits < 6)
        {
            errors.Add("SessionSecurity.OtpDigits must be at least 6.");
        }

        AddPositiveError(
            options.OtpLifetimeMinutes,
            "SessionSecurity.OtpLifetimeMinutes",
            errors);
    }

    private void ValidateAgent(AgentOptions agent, string configFilePath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(agent.StateDirectory))
        {
            errors.Add("Agent.StateDirectory is required.");
            return;
        }

        string stateDirectory;
        try
        {
            stateDirectory = pathResolver.Resolve(agent.StateDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"Agent.StateDirectory is invalid: {exception.Message}");
            return;
        }

        ValidateMachine(agent.Machine, errors);
        ValidateLimits(agent.Limits, errors);
        ValidateRecovery(agent.Recovery, errors);
        ValidateLogging(agent.Logging, errors);
        ValidateWorkspaces(agent, configFilePath, stateDirectory, errors);
    }

    private static void ValidateMachine(MachineOptions machine, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(machine.Id))
        {
            errors.Add("Agent.Machine.Id is required. Use 'auto' for automatic machine identity.");
        }

        if (string.IsNullOrWhiteSpace(machine.DisplayName))
        {
            errors.Add("Agent.Machine.DisplayName is required. Use 'auto' for the local machine name.");
        }
    }

    private static void ValidateLimits(AgentLimitsOptions limits, List<string> errors)
    {
        AddPositiveError(limits.MaxEditableFileBytes, "Agent.Limits.MaxEditableFileBytes", errors);
        AddPositiveError(limits.MaxReadResponseBytes, "Agent.Limits.MaxReadResponseBytes", errors);
        AddPositiveError(limits.MaxDirectoryEntries, "Agent.Limits.MaxDirectoryEntries", errors);
        AddPositiveError(limits.MaxSearchResults, "Agent.Limits.MaxSearchResults", errors);
        AddPositiveError(limits.MaxTraversalDepth, "Agent.Limits.MaxTraversalDepth", errors);
        AddPositiveError(limits.OperationTimeoutSeconds, "Agent.Limits.OperationTimeoutSeconds", errors);
        AddPositiveError(limits.MaxConcurrentMutations, "Agent.Limits.MaxConcurrentMutations", errors);

        if (limits.MaxReadResponseBytes > limits.MaxEditableFileBytes)
        {
            errors.Add("Agent.Limits.MaxReadResponseBytes cannot exceed MaxEditableFileBytes.");
        }
    }

    private static void ValidateRecovery(RecoveryOptions recovery, List<string> errors)
    {
        AddPositiveError(recovery.RetentionDays, "Agent.Recovery.RetentionDays", errors);
        AddPositiveError(recovery.MaxStorageBytes, "Agent.Recovery.MaxStorageBytes", errors);
    }

    private static void ValidateLogging(AgentLoggingOptions logging, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(logging.MinimumLevel))
        {
            errors.Add("Agent.Logging.MinimumLevel is required.");
            return;
        }

        var knownLevels = new[]
        {
            "Trace",
            "Debug",
            "Information",
            "Warning",
            "Error",
            "Critical",
            "None"
        };

        if (!knownLevels.Contains(logging.MinimumLevel, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Agent.Logging.MinimumLevel '{logging.MinimumLevel}' is not supported. " +
                $"Use one of: {string.Join(", ", knownLevels)}.");
        }
    }

    private void ValidateWorkspaces(
        AgentOptions agent,
        string configFilePath,
        string stateDirectory,
        List<string> errors)
    {
        var caseInsensitiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedConfigFilePath = pathResolver.Resolve(configFilePath);

        foreach (var pair in agent.Workspaces)
        {
            var workspaceId = pair.Key;
            var workspace = pair.Value;

            if (!caseInsensitiveIds.Add(workspaceId))
            {
                errors.Add($"Workspace id '{workspaceId}' is duplicated when compared case-insensitively.");
                continue;
            }

            if (!WorkspaceIdPattern.IsMatch(workspaceId))
            {
                errors.Add(
                    $"Workspace id '{workspaceId}' must be 1-64 characters and contain only letters, digits, '.', '_' or '-'.");
            }

            if (string.IsNullOrWhiteSpace(workspace.Root))
            {
                errors.Add($"Workspace '{workspaceId}' requires Root.");
                continue;
            }

            string root;
            try
            {
                root = pathResolver.Resolve(workspace.Root);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"Workspace '{workspaceId}' has an invalid Root: {exception.Message}");
                continue;
            }

            if (workspace.Enabled && !Directory.Exists(root))
            {
                errors.Add($"Enabled workspace '{workspaceId}' root does not exist: {root}");
            }

            if (workspace.Enabled && IsSameOrDescendant(stateDirectory, root))
            {
                errors.Add(
                    $"Agent.StateDirectory must remain outside enabled workspace '{workspaceId}'. State directory: {stateDirectory}");
            }

            if (workspace.Enabled && IsSameOrDescendant(resolvedConfigFilePath, root))
            {
                errors.Add(
                    $"The live configuration file must remain outside enabled workspace '{workspaceId}'. Config file: {resolvedConfigFilePath}");
            }

            ValidateAllowedOperations(workspaceId, workspace, errors);
        }
    }

    private static void ValidateAllowedOperations(
        string workspaceId,
        WorkspaceOptions workspace,
        List<string> errors)
    {
        if (workspace.Enabled && workspace.AllowedOperations.Count == 0)
        {
            errors.Add($"Enabled workspace '{workspaceId}' must have at least one AllowedOperations entry.");
            return;
        }

        foreach (var operation in workspace.AllowedOperations)
        {
            if (!Enum.TryParse<WorkspaceOperation>(operation, ignoreCase: true, out _))
            {
                errors.Add($"Workspace '{workspaceId}' contains unknown operation '{operation}'.");
            }
        }
    }

    private static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        var normalizedCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidatePath));
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(rootPath));

        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void AddPositiveError(long value, string propertyName, List<string> errors)
    {
        if (value <= 0)
        {
            errors.Add($"{propertyName} must be greater than zero.");
        }
    }
}
