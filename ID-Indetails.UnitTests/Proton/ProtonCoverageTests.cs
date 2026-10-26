using System.Reflection;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>
/// "Every action a user can do" as a checked list: each write endpoint of the API has exactly one entry saying how
/// Proton handles it, and the entries can't promise less safety than the endpoint calls for.
/// </summary>
public class ProtonCoverageTests
{
    private sealed record WriteEndpoint(string Name, Type Controller, MethodInfo Method, IReadOnlyList<string> Permissions, string? RatePolicy, bool IsDelete);

    private static readonly IReadOnlyList<WriteEndpoint> Endpoints = typeof(ProtonCoverage).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
        .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => a is not HttpGetAttribute))
            .Select(m => new WriteEndpoint(
                $"{c.Name.Replace("Controller", "")}.{m.Name}",
                c,
                m,
                c.GetCustomAttributes<AuthorizeAttribute>().Concat(m.GetCustomAttributes<AuthorizeAttribute>())
                    .Select(a => a.Policy)
                    .OfType<string>()
                    .Where(p => p.StartsWith(AuthorizationPolicies.PermissionPrefix))
                    .SelectMany(p => p[AuthorizationPolicies.PermissionPrefix.Length..].Split(AuthorizationPolicies.AnyOfSeparator))
                    .ToList(),
                (m.GetCustomAttribute<EnableRateLimitingAttribute>() ?? c.GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName,
                m.GetCustomAttribute<HttpDeleteAttribute>() is not null || m.Name.StartsWith("Delete") || m.Name.StartsWith("Remove"))))
        .ToList();

    private static readonly IReadOnlyDictionary<string, ProtonCoverageEntry> Entries = ProtonCoverage.Entries.ToDictionary(e => e.Endpoint);

    private static IEnumerable<(WriteEndpoint Endpoint, ProtonCoverageEntry Entry)> Actions()
        => Endpoints.Select(e => (e, Entries[e.Name])).Where(x => x.Item2.Kind is ProtonCoverageKind.Action or ProtonCoverageKind.Planned);

    [Fact]
    public void Every_write_endpoint_has_exactly_one_entry()
    {
        ProtonCoverage.Entries.Select(e => e.Endpoint).ShouldBeUnique();
        ProtonCoverage.Entries.Select(e => e.Endpoint).ShouldBe(Endpoints.Select(e => e.Name), ignoreOrder: true,
            customMessage: "Add an entry to Proton/Actions/ProtonCoverage.cs for every new write endpoint (and remove the entry of a removed one).");
    }

    [Fact]
    public void The_count_of_each_kind_is_what_the_plan_says()
    {
        int Count(ProtonCoverageKind kind) => ProtonCoverage.Entries.Count(e => e.Kind == kind);

        Count(ProtonCoverageKind.Action).ShouldBe(89);
        Count(ProtonCoverageKind.Planned).ShouldBe(0);
        ProtonCoverage.Entries.Count(e => e is { Kind: ProtonCoverageKind.Planned, Tier: ProtonTier.Standard }).ShouldBe(0);
        Count(ProtonCoverageKind.Prefill).ShouldBe(2);
        Count(ProtonCoverageKind.Open).ShouldBe(26);
        Count(ProtonCoverageKind.ReadOnly).ShouldBe(3);
        Count(ProtonCoverageKind.Never).ShouldBe(79);
        Count(ProtonCoverageKind.Own).ShouldBe(11);
    }

    [Fact]
    public void Each_built_action_stands_for_its_endpoints_and_only_those()
    {
        var tools = ProtonTestTools.All.OfType<IProtonActionTool>().ToDictionary(t => t.Name);
        var byTool = ProtonCoverage.Entries.Where(e => e.Kind == ProtonCoverageKind.Action).GroupBy(e => e.Tool!).ToList();

        byTool.Select(g => g.Key).ShouldBe(tools.Keys, ignoreOrder: true);
        foreach (var group in byTool)
        {
            var tool = tools[group.Key];
            group.Select(e => e.Endpoint).ShouldContain(tool.Endpoint.Name, group.Key);
            // a tool for several endpoints (hold/resume, done/undone) is checked against each one's own policies: its own
            // endpoint's, or a step endpoint's, which the tool then requires as well for that step
            var allowed = new[] { tool.Endpoint }.Concat(tool.StepEndpoints)
                .Select(s => Endpoints.Single(e => e.Name == s.Name).Permissions.Order().ToList())
                .ToList();
            foreach (var entry in group)
            {
                var permissions = Endpoints.Single(e => e.Name == entry.Endpoint).Permissions.Order().ToList();
                allowed.ShouldContain(a => a.SequenceEqual(permissions), entry.Endpoint);
            }
        }
    }

    [Fact]
    public void Actions_carry_their_tier_and_family_and_only_Core_is_standard()
    {
        foreach (var (endpoint, entry) in Actions())
        {
            entry.Tier.ShouldNotBeNull(endpoint.Name);
            entry.Family.ShouldNotBeNull(endpoint.Name);
            (entry.Tier == ProtonTier.Standard).ShouldBe(entry.Family == ProtonFamily.Core, endpoint.Name);
        }

        ProtonCoverage.Entries.Where(e => e.Kind is not (ProtonCoverageKind.Action or ProtonCoverageKind.Planned))
            .ShouldAllBe(e => e.Tier == null && e.Family == null && e.Tool == null);
        ProtonCoverage.Entries.Where(e => e.Kind is ProtonCoverageKind.Prefill or ProtonCoverageKind.Open or ProtonCoverageKind.ReadOnly or ProtonCoverageKind.Never)
            .ShouldAllBe(e => !string.IsNullOrWhiteSpace(e.Note));
    }

    /// <summary>A sensitive permission makes an action strong, in the family that switches that kind of harm on.</summary>
    [Fact]
    public void An_action_needing_a_sensitive_permission_is_strong_in_the_matching_family()
    {
        var sensitivity = PermissionCatalog.Definitions.ToDictionary(d => d.Code, d => d.Sensitivity);
        var familyOf = new Dictionary<PermissionSensitivity, ProtonFamily>
        {
            [PermissionSensitivity.Money] = ProtonFamily.Money,
            [PermissionSensitivity.Pay] = ProtonFamily.PayRules,
            [PermissionSensitivity.Access] = ProtonFamily.Access,
            [PermissionSensitivity.CompanyRules] = ProtonFamily.CompanyRules,
        };

        foreach (var (endpoint, entry) in Actions())
        {
            var families = endpoint.Permissions.Select(p => sensitivity[p]).Where(s => s != PermissionSensitivity.None).Select(s => familyOf[s]).ToHashSet();
            if (families.Count > 0)
            {
                entry.Tier.ShouldBe(ProtonTier.Strong, endpoint.Name);
                families.ShouldContain(entry.Family!.Value, endpoint.Name);
            }
        }
    }

    /// <summary>A delete that can't be undone is confirmed strongly; a delete with a restore beside it may stay standard.</summary>
    [Fact]
    public void A_delete_without_a_restore_is_strong()
    {
        // resets a person's own board layout to the default: a preference, not a record
        string[] preferences = ["BoardLayouts.Delete"];
        foreach (var (endpoint, entry) in Actions().Where(x => x.Endpoint.IsDelete && !preferences.Contains(x.Endpoint.Name)))
        {
            var restorable = Endpoints.Any(e => e.Controller == endpoint.Controller && e.Method.Name == "Restore");
            if (!restorable)
            {
                entry.Tier.ShouldBe(ProtonTier.Strong, endpoint.Name);
            }
        }
    }

    /// <summary>Only Core and Lifecycle actions may start from a click or a suggested reply; an unclassified tool may not.</summary>
    [Fact]
    public void Only_core_and_lifecycle_actions_may_start_on_a_turn_the_user_did_not_type()
    {
        foreach (var entry in ProtonCoverage.Entries.Where(e => e.Kind == ProtonCoverageKind.Action))
        {
            ProtonCoverage.NeedsTypedTurn(entry.Tool!).ShouldBe(entry.Family is not (ProtonFamily.Core or ProtonFamily.Lifecycle), entry.Tool);
        }

        ProtonCoverage.NeedsTypedTurn("propose_something_new").ShouldBeTrue();
    }

    /// <summary>
    /// Endpoints with their own rate limit send something outside the app (emails, bug reports) or take credentials:
    /// none of them is a Proton action, and every one that emails someone says so.
    /// </summary>
    [Fact]
    public void No_action_wraps_a_rate_limited_endpoint_and_every_email_is_flagged()
    {
        foreach (var (endpoint, _) in Actions())
        {
            endpoint.RatePolicy.ShouldBeNull(endpoint.Name);
        }

        string[] sends = [RateLimitPolicies.Email, RateLimitPolicies.BugReport];
        foreach (var endpoint in Endpoints.Where(e => sends.Contains(e.RatePolicy)))
        {
            Entries[endpoint.Name].ExternalSend.ShouldBeTrue($"{endpoint.Name} emails someone: flag it ExternalSend");
        }

        ProtonCoverage.Entries.Where(e => e.ExternalSend).ShouldAllBe(e => e.Kind != ProtonCoverageKind.Action && e.Kind != ProtonCoverageKind.Planned);
    }
}
