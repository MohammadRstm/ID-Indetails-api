using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Contact the developer: a message from anyone signed in (staff or a portal client), emailed to the developer.</summary>
public class BugReportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string TraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private string DeveloperAddress => api.Services.GetRequiredService<IOptions<BugReportOptions>>().Value.ToAddress;

    private static object Message(string description = "I tried to stop the timer.\nIt said something went wrong.", string? page = "/design/projects/4") => new
    {
        description,
        details = "It was at 12 minutes.",
        page,
        installedApp = true,
    };

    [Fact]
    public async Task Anyone_signed_in_emails_the_developer_with_who_and_where_their_words_encoded_and_replies_going_to_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var sent = await Client.PostJsonAsync("/api/bug-reports", Message("<script>alert(1)</script> broke"), employee.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var message = api.Emails.LastTo(DeveloperAddress);
        message.Subject.ShouldEndWith("message from Test employee");
        message.HtmlBody.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt; broke");
        message.HtmlBody.ShouldNotContain("<script>");
        message.TextBody.ShouldContain($"user {employee.User.Id}, {email}");
        message.TextBody.ShouldContain("Page: /design/projects/4");
        message.TextBody.ShouldContain("employee · design");
        message.TextBody.ShouldContain("Anything else:\nIt was at 12 minutes.");
        message.TextBody.ShouldContain("Opened in: the installed app");
        message.ReplyToAddress.ShouldBe(email);
    }

    [Fact]
    public async Task The_sender_gets_a_copy_of_their_own_words_without_what_the_server_adds()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var sent = await Client.PostJsonAsync("/api/bug-reports", new
        {
            description = "The save button did nothing.",
            page = "/design/projects/4",
            error = new { @ref = TraceId, at = "2026-11-02T09:00:00Z" },
        }, employee.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var copy = api.Emails.LastTo(email);
        copy.Subject.ShouldEndWith("your message to the developer");
        copy.TextBody.ShouldContain("The save button did nothing.");
        copy.TextBody.ShouldContain("Error reference: 4bf92f35");
        copy.TextBody.ShouldNotContain($"user {employee.User.Id}");
        copy.TextBody.ShouldNotContain("Browser");
        copy.TextBody.ShouldNotContain(TraceId);
    }

    [Fact]
    public async Task The_error_they_came_from_and_their_recent_errors_come_with_it_so_the_log_lines_can_be_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        var sent = await Client.PostJsonAsync("/api/bug-reports", new
        {
            description = "The overview went blank.",
            page = "/design/overview",
            error = new { @ref = TraceId, at = "2026-11-02T09:00:00Z" },
            recentErrors = new[] { new { @ref = TraceId, status = 500, path = "/api/finance/design/overview", at = "2026-11-02T09:00:00Z" } },
        }, superAdmin.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var message = api.Emails.LastTo(DeveloperAddress);
        message.Subject.ShouldEndWith("(ref 4bf92f35)");
        message.TextBody.ShouldContain($"Reference: {TraceId}");
        message.TextBody.ShouldContain("Search the log for: 4bf92f35");
        message.TextBody.ShouldContain("When: 2026-11-02 09:00:00 UTC");
        message.TextBody.ShouldContain($"{TraceId} · 500 · /api/finance/design/overview · 2026-11-02 09:00:00 UTC");
        message.HtmlBody.ShouldContain("Recent errors in their browser");
    }

    [Fact]
    public async Task A_page_that_failed_in_the_browser_comes_with_the_browsers_own_message()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        var sent = await Client.PostJsonAsync("/api/bug-reports", new
        {
            description = "The project page went blank.",
            page = "/design/projects/4",
            error = new { at = "2026-11-02T09:00:00Z", message = "Cannot read properties of undefined (reading 'name')" },
        }, superAdmin.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var message = api.Emails.LastTo(DeveloperAddress);
        message.Subject.ShouldEndWith("(a page failed)");
        message.TextBody.ShouldContain("The browser's message: Cannot read properties of undefined (reading 'name')");
    }

    [Fact]
    public async Task A_client_in_the_portal_reaches_the_developer_too_and_neither_side_uses_the_others_door()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (client, clientEmail) = await PortalClientAsync(superAdmin);

        var sent = await Client.PostJsonAsync("/api/portal/contact", Message(page: "/portal/projects/4"), client.AccessToken);
        ((int)sent.StatusCode).ShouldBe(204, await sent.Content.ReadAsStringAsync());

        var message = api.Emails.LastTo(DeveloperAddress);
        message.Subject.ShouldEndWith("message from Rita Frem");
        message.TextBody.ShouldContain($"Rita Frem (portal account {client.Account.Id}, {clientEmail})");
        message.TextBody.ShouldContain("Role: client, in the portal");
        message.ReplyToAddress.ShouldBe(clientEmail);
        api.Emails.LastTo(clientEmail).Subject.ShouldEndWith("your message to the developer");

        var before = api.Emails.CountTo(DeveloperAddress);
        ((int)(await Client.PostJsonAsync("/api/portal/contact", Message(), superAdmin.AccessToken)).StatusCode).ShouldBe(401);
        ((int)(await Client.PostJsonAsync("/api/bug-reports", Message(), client.AccessToken)).StatusCode).ShouldBe(401);
        api.Emails.CountTo(DeveloperAddress).ShouldBe(before);
    }

    [Fact]
    public async Task Nobody_signed_out_sends_and_no_link_token_can_travel_with_the_page()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var before = api.Emails.CountTo(DeveloperAddress);

        await (await Client.PostJsonAsync("/api/bug-reports", Message())).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PostJsonAsync("/api/portal/contact", Message())).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
        await (await Client.PostJsonAsync("/api/bug-reports", Message(page: "/reset-password?token=secret"), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/bug-reports", Message(description: " "), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/bug-reports", new { description = "It broke", error = new { at = "2026-11-02T09:00:00Z" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        api.Emails.CountTo(DeveloperAddress).ShouldBe(before);
    }

    [Fact]
    public async Task A_copy_that_fails_still_sends_the_message()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (developerBefore, senderBefore) = (api.Emails.CountTo(DeveloperAddress), api.Emails.CountTo(email));

        api.Emails.FailNextSendTo = email;
        ((int)(await Client.PostJsonAsync("/api/bug-reports", Message(), employee.AccessToken)).StatusCode).ShouldBe(204);

        api.Emails.CountTo(DeveloperAddress).ShouldBe(developerBefore + 1);
        api.Emails.CountTo(email).ShouldBe(senderBefore);
    }

    [Fact]
    public async Task A_mail_failure_says_so_and_each_person_sends_at_most_five_an_hour()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        api.Emails.FailNextSend = true;
        await (await Client.PostJsonAsync("/api/bug-reports", Message(), employee.AccessToken)).ShouldFailWithAsync(503, ErrorCodes.EmailDeliveryFailed);

        for (var i = 0; i < 4; i++)
        {
            ((int)(await Client.PostJsonAsync("/api/bug-reports", Message(), employee.AccessToken)).StatusCode).ShouldBe(204);
        }

        await (await Client.PostJsonAsync("/api/bug-reports", Message(), employee.AccessToken)).ShouldFailWithAsync(429, ErrorCodes.TooManyRequests);
        // someone else still can
        ((int)(await Client.PostJsonAsync("/api/bug-reports", Message(), superAdmin.AccessToken)).StatusCode).ShouldBe(204);
    }

    /// <summary>A Design client invited to a project by email, who has accepted and signed in to the portal.</summary>
    private async Task<(PortalAuthResponse Auth, string Email)> PortalClientAsync(AuthResponse superAdmin)
    {
        var email = AuthTestSteps.NewEmail("client");
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rita Frem",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = email, label = "Office" } },
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();
        var project = await (await Client.PostJsonAsync("/api/projects", new
        {
            title = "Project " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();

        var invited = await Client.PostJsonAsync($"/api/projects/{project.Id}/portal/invitations", new { email }, superAdmin.AccessToken);
        invited.IsSuccessStatusCode.ShouldBeTrue(await invited.Content.ReadAsStringAsync());
        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(email),
            firstName = "Rita",
            lastName = "Frem",
            password = "client-own-passphrase",
        });
        accepted.IsSuccessStatusCode.ShouldBeTrue(await accepted.Content.ReadAsStringAsync());
        return (await accepted.ReadAsync<PortalAuthResponse>(), email);
    }

    private sealed record IdOnly(long Id);
}
