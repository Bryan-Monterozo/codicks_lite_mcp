namespace LocalAgent.Core.Security;

public interface ISessionOtpGenerator
{
    string Generate(int digits);
}
