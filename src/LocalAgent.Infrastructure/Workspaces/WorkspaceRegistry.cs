using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Workspaces;

public sealed class WorkspaceRegistry : IWorkspaceRegistry
{
    private readonly Dictionary<string, WorkspaceDescriptor> _workspaces;
    private readonly IReadOnlyList<WorkspaceDescriptor> _ordered;

    public WorkspaceRegistry(AgentConfiguration configuration, IUserPathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(pathResolver);

        var workspaces = new Dictionary<string, WorkspaceDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in configuration.Agent.Workspaces)
        {
            var operations = pair.Value.AllowedOperations
                .Select(operation => Enum.Parse<WorkspaceOperation>(operation, ignoreCase: true))
                .Distinct()
                .OrderBy(operation => operation)
                .ToArray();

            var descriptor = new WorkspaceDescriptor(
                Id: pair.Key,
                Root: pathResolver.Resolve(pair.Value.Root),
                Enabled: pair.Value.Enabled,
                AllowedOperations: operations);

            workspaces.Add(pair.Key, descriptor);
        }

        _workspaces = workspaces;
        _ordered = workspaces.Values
            .OrderBy(workspace => workspace.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int Count => _workspaces.Count;

    public IReadOnlyList<WorkspaceDescriptor> List() => _ordered;

    public bool TryGet(string workspaceId, out WorkspaceDescriptor? workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        return _workspaces.TryGetValue(workspaceId, out workspace);
    }
}
