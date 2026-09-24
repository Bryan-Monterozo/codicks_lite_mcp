using LocalAgent.Core.Configuration;
using LocalAgent.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace LocalAgent.Host.Configuration;

public sealed class WorkspaceTopologyOptionsValidator(
    WorkspaceTopologyValidator topologyValidator) : IValidateOptions<AgentConfiguration>
{
    public ValidateOptionsResult Validate(string? name, AgentConfiguration options)
    {
        var errors = topologyValidator.Validate(options);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
