using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Security;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspacePermissionEvaluatorTests
{
    [Fact]
    public void Evaluate_AllowsConfiguredReadOperation()
    {
        var evaluator = new WorkspacePermissionEvaluator(new AgentConfiguration());
        var workspace = CreateWorkspace(WorkspaceOperation.Read);

        var result = evaluator.Evaluate(workspace, WorkspaceOperation.Read);

        Assert.True(result.Allowed);
        Assert.Equal(WorkspaceAccessError.None, result.Error);
    }

    [Fact]
    public void Evaluate_AllowsConfiguredExecuteOperation()
    {
        var evaluator = new WorkspacePermissionEvaluator(new AgentConfiguration());
        var workspace = CreateWorkspace(WorkspaceOperation.Execute);

        var result = evaluator.Evaluate(workspace, WorkspaceOperation.Execute);

        Assert.True(result.Allowed);
        Assert.Equal(WorkspaceAccessError.None, result.Error);
    }

    [Fact]
    public void Evaluate_DeniesOperationNotConfiguredForWorkspace()
    {
        var evaluator = new WorkspacePermissionEvaluator(new AgentConfiguration());
        var workspace = CreateWorkspace(WorkspaceOperation.Read);

        var result = evaluator.Evaluate(workspace, WorkspaceOperation.Update);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.OperationDenied, result.Error);
    }

    [Fact]
    public void Evaluate_GlobalReadOnlyDeniesMutations()
    {
        var configuration = new AgentConfiguration
        {
            Agent = new AgentOptions
            {
                ReadOnly = true
            }
        };

        var evaluator = new WorkspacePermissionEvaluator(configuration);
        var workspace = CreateWorkspace(WorkspaceOperation.Read, WorkspaceOperation.Update);

        var result = evaluator.Evaluate(workspace, WorkspaceOperation.Update);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.GlobalReadOnly, result.Error);
    }

    private static WorkspaceDescriptor CreateWorkspace(params WorkspaceOperation[] operations) =>
        new("demo", "/tmp/demo", true, operations);
}
