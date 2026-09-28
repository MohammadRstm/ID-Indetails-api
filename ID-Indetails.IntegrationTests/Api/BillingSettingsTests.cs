using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class BillingSettingsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static object Settings(string prefix) => new
    {
        invoicePrefix = prefix,
        defaultTermsDays = 45,
        bankAccount = "001-234567",
        bankName = "Bank of Beirut",
        iban = "lb62 0999 0000 0001 0019 0122 9114",
        swift = "bbbelbbe",
        footer = "Thank you",
    };

    [Fact]
    public async Task Admin_sets_up_and_changes_their_domain_billing_settings()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await Client.GetAsync("/api/billing-settings/design", admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.BillingSettingsMissing);

        var saved = await (await Client.PutJsonAsync("/api/billing-settings/design", Settings("HL"), admin.AccessToken))
            .ReadAsync<BillingSettingsResponse>();
        saved.InvoicePrefix.ShouldBe("HL");
        saved.Iban.ShouldBe("LB62099900000001001901229114");
        saved.Swift.ShouldBe("BBBELBBE");

        var changed = await (await Client.PutJsonAsync("/api/billing-settings/design", Settings("HLD"), admin.AccessToken))
            .ReadAsync<BillingSettingsResponse>();
        changed.InvoicePrefix.ShouldBe("HLD");
        (await (await Client.GetAsync("/api/billing-settings/design", admin.AccessToken)).ReadAsync<BillingSettingsResponse>())
            .DefaultTermsDays.ShouldBe(45);

        // Numbers are unique across the company, so the other domain can't take the same prefix.
        await (await Client.PutJsonAsync("/api/billing-settings/execution", Settings("HLD"), superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.InvoicePrefixTaken);
        await (await Client.PutJsonAsync("/api/billing-settings/design", Settings("hl-1"), admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Other_domains_read_as_not_found_and_employees_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.PutJsonAsync("/api/billing-settings/execution", Settings("EX"), designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/billing-settings/company", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/billing-settings/design", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/exchange-rates", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Only_super_admins_add_dated_exchange_rates()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        var added = await Client.PostJsonAsync("/api/exchange-rates", new { currency = "lbp", ratePerUsd = 89_700m, effectiveFrom = "2026-07-01" }, superAdmin.AccessToken);
        ((int)added.StatusCode).ShouldBe(201, await added.Content.ReadAsStringAsync());
        (await added.ReadAsync<ExchangeRateResponse>()).Currency.ShouldBe("LBP");

        await (await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_000m, effectiveFrom = "2026-07-01" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.RateNotAfterLatest);
        await (await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_800m }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM activity_events WHERE kind = 'exchange_rate_set' AND domain_code IS NULL"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Exchange_rates_are_listed_newest_first_per_currency()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await using (var connection = await api.Database.OpenConnectionAsync())
        {
            await using var insert = new MySqlConnector.MySqlCommand(
                "INSERT INTO exchange_rates (currency, rate_per_usd, effective_from, created_at) VALUES " +
                "('EUR', 0.91, '2026-01-01', UTC_TIMESTAMP()), ('EUR', 0.92, '2026-06-01', UTC_TIMESTAMP())", connection);
            await insert.ExecuteNonQueryAsync();
        }

        // Another test in this class adds LBP rates through the API, so this one lists a currency of its own.
        var rates = await (await Client.GetAsync("/api/exchange-rates?currency=eur", superAdmin.AccessToken))
            .ReadAsync<List<ExchangeRateResponse>>();
        rates.Select(r => r.RatePerUsd).ShouldBe([0.92m, 0.91m]);
    }
}
