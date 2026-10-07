using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>
/// The existing API endpoint a tool stands in for. A tool may run only when the user passes that endpoint's own
/// authorization policies — read from its <c>[HasPermission]</c>/<c>[Authorize]</c> attributes, so Proton never keeps
/// a second copy of who may see or do what. Every attribute must pass, as in ASP.NET Core.
/// </summary>
public sealed record ProtonEndpoint(string Name, IReadOnlyList<string> Policies, bool Writes)
{
    /// <summary>A GET endpoint, for read tools: they can't stand in for anything that writes.</summary>
    public static ProtonEndpoint Of<TController>(string action) where TController : ControllerBase
        => Build<TController>(action, writes: false);

    /// <summary>A write endpoint, for action tools: whose policies the user must pass to propose and again to confirm.</summary>
    public static ProtonEndpoint OfWrite<TController>(string action) where TController : ControllerBase
        => Build<TController>(action, writes: true);

    private static ProtonEndpoint Build<TController>(string action, bool writes) where TController : ControllerBase
    {
        var methods = typeof(TController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == action)
            .ToList();
        if (methods.Count != 1)
        {
            throw new InvalidOperationException($"{typeof(TController).Name}.{action} must be exactly one action.");
        }

        var isGet = methods[0].GetCustomAttribute<HttpGetAttribute>() is not null;
        if (isGet == writes)
        {
            throw new InvalidOperationException(writes
                ? $"{typeof(TController).Name}.{action} is a GET endpoint, not one that changes data."
                : $"{typeof(TController).Name}.{action} is not a GET endpoint; read tools only read.");
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

        return new ProtonEndpoint($"{typeof(TController).Name.Replace("Controller", "")}.{action}", policies, writes);
    }
}
