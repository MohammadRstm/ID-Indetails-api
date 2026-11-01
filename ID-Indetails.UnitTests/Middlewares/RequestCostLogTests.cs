using ID_Indetails.api.Middlewares;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace ID_Indetails.UnitTests.Middlewares;

/// <summary>The request cost log never writes the live connection's access token.</summary>
public class RequestCostLogTests
{
    private static string Logged(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return RequestCostMiddleware.LoggedQuery(context.Request);
    }

    [Fact]
    public void An_access_token_in_the_query_string_is_hidden()
    {
        var logged = Logged("?id=abc&access_token=eyJhbGciOiJIUzI1NiJ9.secret.signature");

        logged.ShouldNotContain("eyJ");
        logged.ShouldNotContain("secret");
        logged.ShouldContain("access_token=hidden");
        logged.ShouldContain("id=abc");
    }

    [Fact]
    public void Any_other_query_string_is_logged_as_it_came()
    {
        Logged("?domain=design&months=6").ShouldBe("?domain=design&months=6");
        Logged("").ShouldBe("");
    }
}
