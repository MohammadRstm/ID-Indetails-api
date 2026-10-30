using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Company;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>A payment can be filed under one of the company's account labels, set when it is recorded and changed or cleared after.</summary>
public class PaymentLabelTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<AccountLabelResponse> AddLabelAsync(string accessToken)
    {
        var response = await Client.PostJsonAsync("/api/account-labels", new { name = "Label " + Guid.NewGuid().ToString("N")[..8] }, accessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AccountLabelResponse>();
    }

    private async Task<PaymentResponse> RecordAsync(long projectId, string accessToken, object body)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/payments", body, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PaymentResponse>();
    }

    private Task<HttpResponseMessage> SetLabelAsync(long paymentId, string accessToken, long? accountLabelId)
        => Client.PutJsonAsync($"/api/payments/{paymentId}/label", new { accountLabelId }, accessToken);

    private async Task<PaymentResponse> LabelledAsync(long paymentId, string accessToken, long? accountLabelId)
    {
        var response = await SetLabelAsync(paymentId, accessToken, accountLabelId);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PaymentResponse>();
    }

    private async Task<PaymentResponse> GetAsync(long paymentId, string accessToken)
        => await (await Client.GetAsync($"/api/payments/{paymentId}", accessToken)).ReadAsync<PaymentResponse>();

    [Fact]
    public async Task A_payment_recorded_under_a_label_names_it_and_one_recorded_without_has_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var label = await AddLabelAsync(superAdmin.AccessToken);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var filed = await RecordAsync(site.Id, superAdmin.AccessToken, new { amount = 300m, currency = "USD", accountLabelId = label.Id });
        var plain = await RecordAsync(site.Id, superAdmin.AccessToken, new { amount = 200m, currency = "USD" });
        // labels are the company's: a Design payment takes one too
        var designFiled = await RecordAsync(design.Id, superAdmin.AccessToken, new { amount = 100m, currency = "USD", accountLabelId = label.Id });

        filed.AccountLabel.ShouldNotBeNull();
        filed.AccountLabel.Id.ShouldBe(label.Id);
        filed.AccountLabel.Name.ShouldBe(label.Name);
        plain.AccountLabel.ShouldBeNull();
        designFiled.AccountLabel!.Id.ShouldBe(label.Id);

        // read back by id and through the list
        (await GetAsync(filed.Id, superAdmin.AccessToken)).AccountLabel!.Name.ShouldBe(label.Name);
        var listed = await (await Client.GetAsync($"/api/payments?projectId={site.Id}", superAdmin.AccessToken))
            .ReadAsync<ID_Indetails.api.DTO.Response.Common.PagedResponse<PaymentResponse>>();
        listed.Items.Single(p => p.Id == filed.Id).AccountLabel!.Id.ShouldBe(label.Id);
        listed.Items.Single(p => p.Id == plain.Id).AccountLabel.ShouldBeNull();

        // the JSON carries the label as { id, name } or null
        var json = JsonDocument.Parse(await (await Client.GetAsync($"/api/payments/{filed.Id}", superAdmin.AccessToken)).Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("accountLabel").EnumerateObject().Select(p => p.Name).ShouldBe(["id", "name"]);
        JsonDocument.Parse(await (await Client.GetAsync($"/api/payments/{plain.Id}", superAdmin.AccessToken)).Content.ReadAsStringAsync())
            .RootElement.GetProperty("accountLabel").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_label_of_a_payment_is_set_changed_and_cleared_and_each_change_is_recorded()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var first = await AddLabelAsync(superAdmin.AccessToken);
        var second = await AddLabelAsync(superAdmin.AccessToken);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var payment = await RecordAsync(site.Id, superAdmin.AccessToken, new { amount = 250m, currency = "USD" });

        (await LabelledAsync(payment.Id, superAdmin.AccessToken, first.Id)).AccountLabel!.Id.ShouldBe(first.Id);
        (await LabelledAsync(payment.Id, superAdmin.AccessToken, second.Id)).AccountLabel!.Id.ShouldBe(second.Id);
        // the same label again changes nothing
        (await LabelledAsync(payment.Id, superAdmin.AccessToken, second.Id)).AccountLabel!.Id.ShouldBe(second.Id);
        (await LabelledAsync(payment.Id, superAdmin.AccessToken, null)).AccountLabel.ShouldBeNull();
        // and clearing an unlabelled payment changes nothing either
        (await LabelledAsync(payment.Id, superAdmin.AccessToken, null)).AccountLabel.ShouldBeNull();

        // only the money of a payment is never edited: nothing else about it moved
        var after = await GetAsync(payment.Id, superAdmin.AccessToken);
        after.AmountUsd.ShouldBe(payment.AmountUsd);
        after.ReceivedOn.ShouldBe(payment.ReceivedOn);
        after.VoidedAt.ShouldBeNull();

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'payment_label_set' AND subject_id = @p", ("@p", payment.Id))).ShouldBe(3);
        (await _steps.ScalarAsync<string>(
            "SELECT JSON_UNQUOTE(JSON_EXTRACT(payload, '$.label')) FROM activity_events WHERE kind = 'payment_label_set' AND subject_id = @p ORDER BY id LIMIT 1",
            ("@p", payment.Id))).ShouldBe(first.Name);
        (await _steps.ScalarAsync<string>(
            "SELECT JSON_UNQUOTE(JSON_EXTRACT(payload, '$.previousLabel')) FROM activity_events WHERE kind = 'payment_label_set' AND subject_id = @p ORDER BY id LIMIT 1 OFFSET 1",
            ("@p", payment.Id))).ShouldBe(first.Name);
    }

    [Fact]
    public async Task An_archived_label_cannot_be_newly_set_but_a_payment_keeps_and_can_drop_its_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var keeping = await AddLabelAsync(superAdmin.AccessToken);
        var archived = await AddLabelAsync(superAdmin.AccessToken);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var payment = await RecordAsync(site.Id, superAdmin.AccessToken, new { amount = 100m, currency = "USD", accountLabelId = keeping.Id });
        (await Client.PostEmptyAsync($"/api/account-labels/{keeping.Id}/archive", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/account-labels/{archived.Id}/archive", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // not newly picked: when recording, or when changing a payment that has another
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments",
                new { amount = 10m, currency = "USD", accountLabelId = archived.Id }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.AccountLabelArchived);
        await (await SetLabelAsync(payment.Id, superAdmin.AccessToken, archived.Id)).ShouldFailWithAsync(422, ErrorCodes.AccountLabelArchived);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM payments WHERE project_id = @p", ("@p", site.Id))).ShouldBe(1);

        // the payment keeps its own, still named, and setting it again is fine
        (await GetAsync(payment.Id, superAdmin.AccessToken)).AccountLabel!.Name.ShouldBe(keeping.Name);
        (await LabelledAsync(payment.Id, superAdmin.AccessToken, keeping.Id)).AccountLabel!.Id.ShouldBe(keeping.Id);
        // and can be cleared
        (await LabelledAsync(payment.Id, superAdmin.AccessToken, null)).AccountLabel.ShouldBeNull();
        // but once cleared, the archived label can't be set back
        await (await SetLabelAsync(payment.Id, superAdmin.AccessToken, keeping.Id)).ShouldFailWithAsync(422, ErrorCodes.AccountLabelArchived);
    }

    [Fact]
    public async Task A_label_that_does_not_exist_is_a_validation_error_naming_the_field()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var payment = await RecordAsync(site.Id, superAdmin.AccessToken, new { amount = 100m, currency = "USD" });

        var onRecord = await (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments",
                new { amount = 10m, currency = "USD", accountLabelId = 999_999_999L }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        onRecord.Errors!.Keys.ShouldContain("accountLabelId");

        var onChange = await (await SetLabelAsync(payment.Id, superAdmin.AccessToken, 999_999_999L)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        onChange.Errors!.Keys.ShouldContain("accountLabelId");

        // zero and negative ids never reach the lookup
        await (await SetLabelAsync(payment.Id, superAdmin.AccessToken, 0)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments",
                new { amount = 10m, currency = "USD", accountLabelId = -1 }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM payments WHERE project_id = @p", ("@p", site.Id))).ShouldBe(1);
        (await GetAsync(payment.Id, superAdmin.AccessToken)).AccountLabel.ShouldBeNull();
    }

    [Fact]
    public async Task A_voided_payment_cannot_be_relabelled()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var label = await AddLabelAsync(superAdmin.AccessToken);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var payment = await RecordAsync(site.Id, superAdmin.AccessToken, new { amount = 100m, currency = "USD", accountLabelId = label.Id });
        (await Client.PostJsonAsync($"/api/payments/{payment.Id}/void", new { reason = "Entered twice" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        await (await SetLabelAsync(payment.Id, superAdmin.AccessToken, null)).ShouldFailWithAsync(422, ErrorCodes.PaymentAlreadyVoided);
        (await GetAsync(payment.Id, superAdmin.AccessToken)).AccountLabel!.Id.ShouldBe(label.Id);
    }

    [Fact]
    public async Task Recording_and_relabelling_need_the_permission_to_record_payments_in_a_domain_the_caller_sees()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var label = await AddLabelAsync(superAdmin.AccessToken);
        var (_, _, recorder) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var (_, _, otherDomain) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, viewer) = await AdminWithAsync(superAdmin, DomainCodes.Execution, PermissionCodes.FinancialView);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var payment = await RecordAsync(site.Id, recorder.AccessToken, new { amount = 100m, currency = "USD" });

        (await LabelledAsync(payment.Id, recorder.AccessToken, label.Id)).AccountLabel!.Id.ShouldBe(label.Id);
        (await RecordAsync(site.Id, recorder.AccessToken, new { amount = 5m, currency = "USD", accountLabelId = label.Id })).AccountLabel.ShouldNotBeNull();

        // seeing money is not enough, and an employee has neither
        await (await SetLabelAsync(payment.Id, viewer.AccessToken, null)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await SetLabelAsync(payment.Id, employee.AccessToken, null)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        // nor is recording payments without seeing money: the answer is the whole payment
        var (_, _, blindRecorder) = await AdminWithAsync(superAdmin, DomainCodes.Execution, PermissionCodes.PaymentRecord);
        await (await SetLabelAsync(payment.Id, blindRecorder.AccessToken, label.Id)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments",
                new { amount = 5m, currency = "USD", accountLabelId = label.Id }, viewer.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // a payment of a domain the caller can't see reads as missing
        await (await SetLabelAsync(payment.Id, otherDomain.AccessToken, null)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await SetLabelAsync(999_999_999L, superAdmin.AccessToken, null)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        (await GetAsync(payment.Id, superAdmin.AccessToken)).AccountLabel!.Id.ShouldBe(label.Id);
    }

    /// <summary>An admin of <paramref name="domain"/> given exactly <paramref name="codes"/> beyond what an admin holds.</summary>
    private async Task<(string Email, string Password, AuthResponse Auth)> AdminWithAsync(AuthResponse superAdmin, string domain, params string[] codes)
    {
        var admin = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, domain);
        var granted = await Client.PutJsonAsync($"/api/users/{admin.Auth.User.Id}/permissions",
            new { changes = codes.Select(code => new { code, action = "grant", reason = "Test" }) }, superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        return admin;
    }
}
