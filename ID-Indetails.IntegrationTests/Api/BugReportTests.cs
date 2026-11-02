using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class BugReportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private string SupportAddress => api.Services.GetRequiredService<IOptions<BugReportOptions>>().Value.ToAddress;

    private static object Report(string title = "The timer stopped", string? page = "/design/projects/4") => new
    {
        title,
        description = "I changed page and it stopped.\nIt was at 12 minutes.",
        steps = "1. Start the timer\n2. Open a project",
        page,
    };

    [Fact]
    public async Task Anyone_signed_in_reports_a_bug_by_email_with_who_and_where_and_their_words_encoded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var sent = await Client.PostJsonAsync("/api/bug-reports", Report("<script>alert(1)</script> broke"), employee.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var message = api.Emails.LastTo(SupportAddress);
        message.Subject.ShouldEndWith("<script>alert(1)</script> broke");
        message.HtmlBody.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt; broke");
        message.HtmlBody.ShouldNotContain("<script>");
        message.TextBody.ShouldContain($"user {employee.User.Id}, {email}");
        message.TextBody.ShouldContain("Page: /design/projects/4");
        message.TextBody.ShouldContain("employee · design");
    }

    [Fact]
    public async Task The_recent_errors_in_their_browser_come_with_the_report_so_the_log_lines_can_be_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        const string reference = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

        var sent = await Client.PostJsonAsync("/api/bug-reports", new
        {
            title = "The overview went blank",
            description = "It said something went wrong.",
            page = "/design/overview",
            recentErrors = new[] { new { @ref = reference, status = 500, path = "/api/finance/design/overview", at = "2026-11-02T09:00:00Z" } },
        }, superAdmin.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var message = api.Emails.LastTo(SupportAddress);
        message.TextBody.ShouldContain($"{reference} · 500 · /api/finance/design/overview · 2026-11-02 09:00:00 UTC");
        message.HtmlBody.ShouldContain("Recent errors in their browser");
    }

    [Fact]
    public async Task Nobody_signed_out_reports_and_no_link_token_can_travel_with_the_page()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var before = api.Emails.CountTo(SupportAddress);

        await (await Client.PostJsonAsync("/api/bug-reports", Report())).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PostJsonAsync("/api/bug-reports", Report(page: "/reset-password?token=secret"), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/bug-reports", Report(title: "two\nlines"), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/bug-reports", Report(title: new string('x', 121)), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        api.Emails.CountTo(SupportAddress).ShouldBe(before);
    }

    [Fact]
    public async Task A_mail_failure_says_so_and_each_person_sends_at_most_five_an_hour()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        api.Emails.FailNextSend = true;
        await (await Client.PostJsonAsync("/api/bug-reports", Report(), employee.AccessToken)).ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);

        for (var i = 0; i < 4; i++)
        {
            ((int)(await Client.PostJsonAsync("/api/bug-reports", Report(), employee.AccessToken)).StatusCode).ShouldBe(204);
        }

        await (await Client.PostJsonAsync("/api/bug-reports", Report(), employee.AccessToken)).ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        // someone else still can
        ((int)(await Client.PostJsonAsync("/api/bug-reports", Report(), superAdmin.AccessToken)).StatusCode).ShouldBe(204);
    }
}
