using System.Security.Claims;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace ID_Indetails.UnitTests.Middlewares;

/// <summary>What the request logs write: failed and slow requests, who they are from, and never an access token.</summary>
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

    [Theory]
    [InlineData(500, 30, false, LogLevel.Error)]
    [InlineData(503, 30, true, LogLevel.Error)]
    [InlineData(200, 1500, false, LogLevel.Warning)]
    [InlineData(404, 1000, false, LogLevel.Warning)]
    public void Failed_and_slow_requests_are_logged_wherever_the_api_runs(int status, long milliseconds, bool streaming, LogLevel expected)
        => RequestCostMiddleware.LevelOf(all: false, status, milliseconds, 1000, streaming).ShouldBe(expected);

    [Fact]
    public void A_quick_request_or_an_open_live_connection_is_logged_only_when_every_request_is()
    {
        RequestCostMiddleware.LevelOf(all: false, 200, 40, 1000, streaming: false).ShouldBeNull();
        // a live connection or a stream stays open for minutes by design
        RequestCostMiddleware.LevelOf(all: false, 101, 600_000, 1000, streaming: true).ShouldBeNull();
        RequestCostMiddleware.LevelOf(all: true, 200, 40, 1000, streaming: false).ShouldBe(LogLevel.Information);
    }

    [Fact]
    public void Each_line_names_who_the_request_is_from_once_they_are_signed_in()
    {
        var context = new DefaultHttpContext();
        var scope = new RequestLogScope(context);
        scope.ShouldAllBe(f => f.Value == null);

        // sign-in runs after the scope opens: the lines written from then on name the person
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaimTypes.UserId, "42")], "Bearer"));
        scope.Single(f => f.Key == "UserId").Value.ShouldBe("42");
        scope.ToString().ShouldBe("UserId:42");

        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaimTypes.PortalAccountId, "7")], "Portal"));
        scope.Single(f => f.Key == "PortalAccountId").Value.ShouldBe("7");
        scope.Single(f => f.Key == "UserId").Value.ShouldBeNull();
    }

    [Fact]
    public void Work_logging_after_its_request_ended_keeps_who_it_was_from_and_never_reads_the_reused_request()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaimTypes.UserId, "42")], "Bearer")),
        };
        var scope = new RequestLogScope(context);
        scope.Complete();

        // the server hands the same request object to someone else's request
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaimTypes.UserId, "99")], "Bearer"));
        scope.Single(f => f.Key == "UserId").Value.ShouldBe("42");
    }
}
