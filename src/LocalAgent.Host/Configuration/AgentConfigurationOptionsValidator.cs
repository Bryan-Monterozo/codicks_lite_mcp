using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using Microsoft.Extensions.Options;

namespace LocalAgent.Host.Configuration;

public sealed class AgentConfigurationOptionsValidator(
    IUserPathResolver pathResolver,
    ConfigurationRuntimeInfo runtimeInfo) : IValidateOptions<AgentConfiguration>
{
    public ValidateOptionsResult Validate(string? name, AgentConfiguration options)
    {
        var validator = new AgentConfigurationValidator(pathResolver);
        var errors = validator.Validate(options, runtimeInfo.ConfigFilePath);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
