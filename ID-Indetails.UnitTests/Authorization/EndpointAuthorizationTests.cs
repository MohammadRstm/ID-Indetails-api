using System.Reflection;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Shouldly;

namespace ID_Indetails.UnitTests.Authorization;

/// <summary>
/// The fallback policy makes an endpoint without an attribute require sign-in, but every endpoint
/// should still say what it needs, so reviewers can see it.
/// </summary>
public class EndpointAuthorizationTests
{
    /// <summary>Every endpoint anyone can call without signing in. A new one is added here on purpose, or the test fails.</summary>
    private static readonly string[] OpenEndpoints =
    [
        "AuthController.Login", "AuthController.Refresh", "AuthController.Logout", "AuthController.ForgotPassword", "AuthController.ResetPassword",
        "CompanyController.Branding", "CompanyController.Logo",
        "InvitationsController.Validate", "InvitationsController.Accept",
        "LocalContentStoreController.PutPart", "LocalContentStoreController.Download",
        "MobileAuthController.Login", "MobileAuthController.Refresh", "MobileAuthController.Logout",
        "PortalAuthController.Login", "PortalAuthController.Refresh", "PortalAuthController.Logout",
        "PortalAuthController.ForgotPassword", "PortalAuthController.ResetPassword",
        "PortalInvitationsController.Validate", "PortalInvitationsController.Accept",
        "SetupController.CreateSuperAdmin",
    ];

    /// <summary>The client portal's controllers for signing in, which run before the client has a portal token.</summary>
    private static readonly Type[] PortalSignIn = [typeof(PortalAuthController), typeof(PortalInvitationsController)];

    [Fact]
    public void Every_endpoint_declares_its_permission_or_that_it_is_public()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var undeclared = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Where(m => !Declares(m) && !Declares(c))
                .Select(m => $"{c.Name}.{m.Name}"))
            .ToList();

        undeclared.ShouldBeEmpty();
    }

    [Fact]
    public void Every_client_portal_endpoint_requires_a_portal_token_except_signing_in()
    {
        var unprotected = Endpoints()
            .Where(e => IsPortalRoute(e.Controller) && !PortalSignIn.Contains(e.Controller))
            .Where(e => !UsesPortalPolicy(e) || IsOpen(e))
            .Select(Name)
            .ToList();

        unprotected.ShouldBeEmpty();
    }

    [Fact]
    public void No_staff_endpoint_accepts_a_portal_token()
    {
        var crossing = Endpoints()
            .Where(e => !IsPortalRoute(e.Controller) && UsesPortalPolicy(e))
            .Select(Name)
            .ToList();

        crossing.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_listed_endpoints_are_open_to_anyone()
    {
        Endpoints().Where(IsOpen).Select(Name).ShouldBe(OpenEndpoints, ignoreOrder: true);
    }

    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints()
        => typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (Controller: c, Action: m)));

    /// <summary>Under <c>/api/portal</c>: the client's side. Staff manage a project's portal under <c>/api/projects/{id}/portal</c>.</summary>
    private static bool IsPortalRoute(Type controller)
        => controller.GetCustomAttribute<RouteAttribute>()?.Template is { } route
            && (route == "api/portal" || route.StartsWith("api/portal/", StringComparison.Ordinal));

    private static bool UsesPortalPolicy((Type Controller, MethodInfo Action) endpoint)
        => endpoint.Action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(endpoint.Controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Any(a => a.Policy == AuthorizationPolicies.Portal);

    private static bool IsOpen((Type Controller, MethodInfo Action) endpoint)
        => endpoint.Action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
            || endpoint.Controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    private static string Name((Type Controller, MethodInfo Action) endpoint) => $"{endpoint.Controller.Name}.{endpoint.Action.Name}";

    private static bool Declares(MemberInfo member)
        => member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
            || member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
}
