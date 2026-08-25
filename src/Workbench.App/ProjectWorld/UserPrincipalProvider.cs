using Workbench.Core.Continuity;

namespace Workbench.App.ProjectWorld;

public interface IUserPrincipalProvider
{
    UserPrincipalRef GetCurrent();
}

public sealed class LocalUserPrincipalProvider : IUserPrincipalProvider
{
    public UserPrincipalRef GetCurrent()
    {
        var user = Environment.UserName;
        var domain = Environment.UserDomainName;
        var locator = string.IsNullOrWhiteSpace(domain) || string.Equals(domain, user, StringComparison.OrdinalIgnoreCase)
            ? $"user:{user}"
            : $"user:{domain}\\{user}";
        return new UserPrincipalRef(locator);
    }
}
