namespace LocalAgent.Core.Paths;

public interface IUserPathResolver
{
    string Resolve(string configuredPath);
}
