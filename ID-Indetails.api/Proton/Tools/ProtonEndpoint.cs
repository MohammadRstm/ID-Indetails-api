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

    /// <summary>
    /// The endpoints with no permission of their own (everyone has a chat and a bell): who may see what is decided in
    /// their service, for the signed-in person, so Proton reads them as that person. Only those listed here; each
    /// still needs proton:use, and only GETs.
    /// </summary>
    public static readonly IReadOnlySet<string> RuleBased = new HashSet<string>(StringComparer.Ordinal)
    {
        "Messages.Contacts", "Messages.Conversations", "Notifications.List", "Teams.List",
        "Company.GetSettings", "DomainSettings.Get",
        // the discussion with a client decides who takes part in it, from the project, never from a permission
        "ProjectPortalDiscussion.Messages",
    };

    /// <summary>
    /// Write endpoints with no permission of their own: changing the signed-in person's own name and phone, and sending
    /// a message, which their service limits to the chats the sender may write in. proton:use is their policy here.
    /// </summary>
    public static readonly IReadOnlySet<string> RuleBasedWrites = new HashSet<string>(StringComparer.Ordinal)
    {
        "Profile.UpdateProfile", "Messages.SendDirect", "Messages.SendToTeam",
    };

    /// <summary>A write endpoint from <see cref="RuleBasedWrites"/>: its service decides who it may reach.</summary>
    public static ProtonEndpoint OfRuleBasedWrite<TController>(string action) where TController : ControllerBase
    {
        var name = $"{typeof(TController).Name.Replace("Controller", "")}.{action}";
        var method = typeof(TController).GetMethods(BindingFlags.Public | BindingFlags.Instance).SingleOrDefault(m => m.Name == action);
        if (!RuleBasedWrites.Contains(name) || method is null || method.GetCustomAttribute<HttpGetAttribute>() is not null)
        {
            throw new InvalidOperationException($"{name} is not a rule-based write Proton may stand in for.");
        }

        return new ProtonEndpoint(name, [Authorization.AuthorizationPolicies.PermissionPrefix + Constants.PermissionCodes.ProtonUse], true);
    }

    /// <summary>A GET endpoint from <see cref="RuleBased"/>: <c>proton:use</c> is its policy here, and its service decides the rest.</summary>
    public static ProtonEndpoint OfRuleBased<TController>(string action) where TController : ControllerBase
    {
        var name = $"{typeof(TController).Name.Replace("Controller", "")}.{action}";
        var method = typeof(TController).GetMethods(BindingFlags.Public | BindingFlags.Instance).SingleOrDefault(m => m.Name == action);
        if (!RuleBased.Contains(name) || method?.GetCustomAttribute<HttpGetAttribute>() is null)
        {
            throw new InvalidOperationException($"{name} is not a rule-based read Proton may stand in for.");
        }

        return new ProtonEndpoint(name, [Authorization.AuthorizationPolicies.PermissionPrefix + Constants.PermissionCodes.ProtonUse], false);
    }

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
