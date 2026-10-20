using ID_Indetails.api.DTO.Response.Website;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Same API with a fallback address for the website's contact form.</summary>
public sealed class ContactFallbackApiFactory : ApiFactory
{
    public const string Address = "fallback@example.com";

    protected override string ContactFallbackAddress => Address;
}

/// <summary>The contact form's fallback address, used until the company sets its contact email (docs/website.md).</summary>
public class WebsiteContactFallbackTests(ContactFallbackApiFactory api) : IClassFixture<ContactFallbackApiFactory>
{
    private const string Public = "/api/public/website";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Until_the_company_sets_its_contact_email_messages_go_to_the_fallback_address_which_is_never_shown()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var message = new { name = "Nadine Haddad", email = "nadine@example.com", phone = "", message = "We'd like a villa in Batroun.", website = "" };
        (await Client.PutJsonAsync("/api/company/contact-email", new { email = "" }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var toFallback = await Client.PostJsonAsync($"{Public}/contact-messages", message);
        ((int)toFallback.StatusCode).ShouldBe(202, await toFallback.Content.ReadAsStringAsync());
        api.Emails.LastTo(ContactFallbackApiFactory.Address).ReplyToAddress.ShouldBe("nadine@example.com");
        (await (await Client.GetAsync($"{Public}/contact")).ReadAsync<PublicWebsiteContact>()).Email.ShouldBeNull();

        // once the company sets its own address, that address wins
        var address = AuthTestSteps.NewEmail("studio");
        (await Client.PutJsonAsync("/api/company/contact-email", new { email = address }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var fallbackCount = api.Emails.CountTo(ContactFallbackApiFactory.Address);
        var toCompany = await Client.PostJsonAsync($"{Public}/contact-messages", message);
        ((int)toCompany.StatusCode).ShouldBe(202, await toCompany.Content.ReadAsStringAsync());
        api.Emails.CountTo(address).ShouldBe(1);
        api.Emails.CountTo(ContactFallbackApiFactory.Address).ShouldBe(fallbackCount);
    }
}
