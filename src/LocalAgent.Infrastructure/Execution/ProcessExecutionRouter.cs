using LocalAgent.Core.Execution;

namespace LocalAgent.Infrastructure.Execution;

public sealed class ProcessExecutionRouter(
    HostProcessExecutionService hostService,
    SandboxProcessExecutionService sandboxService) : IProcessExecutionService
{
    public Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ExecutionMode switch
        {
            ExecutionMode.Host =>
                hostService.ExecuteAsync(
                    request,
                    cancellationToken),

            ExecutionMode.Sandbox =>
                sandboxService.ExecuteAsync(
                    request,
                    cancellationToken),

            _ => throw new ProcessExecutionException(
                ProcessExecutionError.UnsupportedExecutionMode,
                $"Execution mode '{request.ExecutionMode}' is not supported.")
        };
    }
}
