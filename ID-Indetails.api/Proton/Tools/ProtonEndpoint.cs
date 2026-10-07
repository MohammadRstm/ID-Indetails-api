using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>
/// The existing API endpoint a tool stands in for. A tool may run only when the user passes that endpoint's own
/// authorization policies — read from its <c>[HasPermission]</c>/<c>[Authorize]</c> attributes, so Proton never keeps
/// a second copy of who may see what. Every attribute must pass, as in ASP.NET Core.
/// </summary>
public sealed record ProtonEndpoint(string Name, IReadOnlyList<string> Policies)
{
    public static ProtonEndpoint Of<TController>(string action) where TController : ControllerBase
    {
        var methods = typeof(TController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == action)
            .ToList();
        if (methods.Count != 1)
        {
            throw new InvalidOperationException($"{typeof(TController).Name}.{action} must be exactly one action.");
        }

        // Read-only by construction: a tool can only stand in for an endpoint that reads.
        if (methods[0].GetCustomAttribute<HttpGetAttribute>() is null)
        {
            throw new InvalidOperationException($"{typeof(TController).Name}.{action} is not a GET endpoint; Proton tools only read.");
        }

        var policies = typeof(TController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(methods[0].GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(a => a.Policy)
            .OfType<string>()
            .Distinct()
            .ToList();
        if (policies.Count == 0)
        {
            throw new InvalidOperationException($"{typeof(TController).Name}.{action} has no permission policy for Proton to inherit.");
        }

        return new ProtonEndpoint($"{typeof(TController).Name.Replace("Controller", "")}.{action}", policies);
    }
}
