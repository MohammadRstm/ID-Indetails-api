using System.Reflection;
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

    private static bool Declares(MemberInfo member)
        => member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
            || member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
}
