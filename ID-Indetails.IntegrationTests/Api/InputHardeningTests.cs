using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Malformed input is refused with the uniform 400, never a crash, and the public health check says only that it's up.</summary>
public class InputHardeningTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task An_explicit_null_where_a_list_is_needed_is_a_validation_error()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await Client.PostJsonAsync($"/api/messages/direct/{superAdmin.User.Id}", new { body = "Hi", attachmentIds = (long[]?)null }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_page_too_far_to_count_and_an_email_that_would_add_recipients_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await Client.GetAsync("/api/projects?page=300000000", superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Bcc trick",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = "billing@client.test?bcc=someone%40elsewhere.test" } },
        }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/invitations",
            new { email = "new person@test.local", role = RoleCodes.Employee, domainCode = DomainCodes.Design }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_project_icon_is_an_icon_name()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await Client.PostJsonAsync("/api/projects",
            new { title = "Odd icon", domainCode = DomainCodes.Design, billingType = "internal", icon = "__proto__<b>" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task No_cache_keeps_an_answer_and_no_browser_guesses_its_type()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        foreach (var response in new[] { await Client.GetAsync("/api/auth/me", superAdmin.AccessToken), await Client.GetAsync("/api/auth/me") })
        {
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        }
    }

    [Fact]
    public async Task The_public_health_check_answers_anyone_with_the_status_only()
    {
        var response = await Client.GetAsync("/api/health");

        ((int)response.StatusCode).ShouldBe(200);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }
}
