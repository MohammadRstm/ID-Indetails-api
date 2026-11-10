using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Permissions;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The people, access and settings actions with a scripted model: roles and businesses, permissions, invitations,
/// Proton's company switches, the contact email, account labels, excuses, the Proton budget, and a person's own
/// settings. Each goes through its service, and only on the user's Confirm. Shared company settings are put back.
/// </summary>
public class ProtonPeopleActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    /// <param name="said">What the user typed: money amounts and outside addresses on a card must come from it.</param>
    private async Task<(ProtonReplyResponse Reply, JsonNode Result)> ProposeAsync(AuthResponse who, string tool, object input, string said = "Please make that change.")
    {
        api.Anthropic.Reset();
        api.Anthropic.Reply(FakeAnthropic.ToolUse(tool, input), FakeAnthropic.Text("Here is the change for you to confirm."));
        var response = await Client.PostJsonAsync("/api/proton/messages", new { text = said }, who.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(api.Anthropic.ToolResults(1).Single()["content"]!.GetValue<string>())!;
        return (await response.ReadAsync<ProtonReplyResponse>(), result);
    }

    private async Task<ProtonActionOutcomeResponse> ConfirmAsync(AuthResponse who, ProtonReplyResponse reply, string? typed = null)
    {
        var card = reply.PendingAction.ShouldNotBeNull();
        var response = await Client.PostJsonAsync($"/api/proton/actions/{card.Id}/confirm", new { reply.ConversationId, typed }, who.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProtonActionOutcomeResponse>();
    }

    /// <summary>Confirms a strong card by typing the phrase it asks for.</summary>
    private Task<ProtonActionOutcomeResponse> ConfirmTypedAsync(AuthResponse who, ProtonReplyResponse reply)
        => ConfirmAsync(who, reply, reply.PendingAction.ShouldNotBeNull().Preview.Phrase.ShouldNotBeNull());

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<CompanySettingsResponse> CompanyAsync(AuthResponse who)
        => OkAsync<CompanySettingsResponse>(Client.GetAsync("/api/company/settings", who.AccessToken));

    [Fact]
    public async Task An_employee_is_moved_to_the_other_business_with_their_name_typed_and_never_oneself()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var (card, _) = await ProposeAsync(superAdmin, "propose_employee_access", new { step = "move", user_id = designer.User.Id, domain = "execution" });
        card.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        await ConfirmTypedAsync(superAdmin, card);
        (await OkAsync<EmployeeResponse>(Client.GetAsync($"/api/employees/{designer.User.Id}", superAdmin.AccessToken))).Domain.ShouldBe(DomainCodes.Execution);

        var (selfCard, self) = await ProposeAsync(superAdmin, "propose_employee_access", new { step = "move", user_id = superAdmin.User.Id, domain = "design" });
        selfCard.PendingAction.ShouldBeNull();
        self["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_permission_is_granted_with_the_persons_name_typed_and_a_super_admins_cannot_be_changed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var code = PermissionCodes.InvoiceCreate;
        (await OkAsync<UserPermissionsResponse>(Client.GetAsync($"/api/users/{employee.User.Id}/permissions", superAdmin.AccessToken))).Permissions.ShouldNotContain(code);

        var (card, _) = await ProposeAsync(superAdmin, "propose_permissions",
            new { user_id = employee.User.Id, changes = new[] { new { code, action = "grant" } }, reason = "Helps with billing" });
        card.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        await ConfirmTypedAsync(superAdmin, card);
        (await OkAsync<UserPermissionsResponse>(Client.GetAsync($"/api/users/{employee.User.Id}/permissions", superAdmin.AccessToken))).Permissions.ShouldContain(code);

        var (lockedCard, locked) = await ProposeAsync(superAdmin, "propose_permissions",
            new { user_id = superAdmin.User.Id, changes = new[] { new { code, action = "revoke" } }, reason = "Test" });
        lockedCard.PendingAction.ShouldBeNull();
        locked["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_invitation_is_emailed_only_to_an_address_the_user_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail("invitee");

        var (untypedCard, untyped) = await ProposeAsync(superAdmin, "propose_invitation",
            new { step = "create", email, domain = "design" }, "Invite the new designer");
        untypedCard.PendingAction.ShouldBeNull();
        untyped["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        api.Emails.CountTo(email).ShouldBe(0);

        // a Design employee needs a department, as the invitation form asks
        var (noDepartmentCard, noDepartment) = await ProposeAsync(superAdmin, "propose_invitation",
            new { step = "create", email, domain = "design", position = "Architect" }, $"Invite {email} to Design");
        noDepartmentCard.PendingAction.ShouldBeNull();
        noDepartment["message"]!.GetValue<string>().ShouldContain("department");

        var department = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, $"Studio {Guid.NewGuid():N}"[..20]);
        var (card, _) = await ProposeAsync(superAdmin, "propose_invitation",
            new { step = "create", email, domain = "design", team_id = department.Id, position = "Architect" }, $"Invite {email} to Design");
        card.PendingAction!.Preview.Changes.ShouldContain(c => c.Label == "Department" && c.After == department.Name);
        api.Emails.CountTo(email).ShouldBe(0);
        await ConfirmTypedAsync(superAdmin, card);
        api.Emails.CountTo(email).ShouldBe(1);
    }

    [Fact]
    public async Task Proton_company_switches_and_the_contact_email_are_changed_with_the_company_name_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var original = await CompanyAsync(superAdmin);
        try
        {
            var (card, _) = await ProposeAsync(superAdmin, "propose_company_proton", new { setting = "project_files", enabled = !original.ProtonReadsFiles });
            card.PendingAction!.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
            await ConfirmTypedAsync(superAdmin, card);
            (await CompanyAsync(superAdmin)).ProtonReadsFiles.ShouldBe(!original.ProtonReadsFiles);

            // already so
            var (sameCard, same) = await ProposeAsync(superAdmin, "propose_company_proton", new { setting = "project_files", enabled = !original.ProtonReadsFiles });
            sameCard.PendingAction.ShouldBeNull();
            same["message"]!.GetValue<string>().ShouldContain("already");

            var suggestions = await ProposeAsync(superAdmin, "propose_company_proton", new { setting = "reply_suggestions", enabled = !original.ProtonReplySuggestions });
            await ConfirmTypedAsync(superAdmin, suggestions.Reply);
            (await CompanyAsync(superAdmin)).ProtonReplySuggestions.ShouldBe(!original.ProtonReplySuggestions);

            // an address the user never typed is refused
            var contact = $"hello-{Guid.NewGuid():N}"[..14] + "@studio.test";
            var (untypedCard, _) = await ProposeAsync(superAdmin, "propose_company_contact_email", new { email = contact }, "Change our contact email");
            untypedCard.PendingAction.ShouldBeNull();
            var (contactCard, _) = await ProposeAsync(superAdmin, "propose_company_contact_email", new { email = contact }, $"Our email is {contact}");
            await ConfirmTypedAsync(superAdmin, contactCard);
            (await CompanyAsync(superAdmin)).ContactEmail.ShouldBe(contact);
        }
        finally
        {
            await Client.PutJsonAsync("/api/company/proton-files", new { enabled = original.ProtonReadsFiles }, superAdmin.AccessToken);
            await Client.PutJsonAsync("/api/company/proton-suggestions", new { enabled = original.ProtonReplySuggestions }, superAdmin.AccessToken);
            await Client.PutJsonAsync("/api/company/contact-email", new { email = original.ContactEmail ?? string.Empty }, superAdmin.AccessToken);
        }
    }

    [Fact]
    public async Task An_account_label_is_added_renamed_archived_and_restored_by_name()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = "Label " + Guid.NewGuid().ToString("N")[..8];
        var renamed = name + " b";

        var (add, _) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "create", name });
        await ConfirmAsync(superAdmin, add);
        var labels = await OkAsync<List<AccountLabelResponse>>(Client.GetAsync("/api/account-labels?includeArchived=true", superAdmin.AccessToken));
        var label = labels.Single(l => l.Name == name);

        var (rename, _) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "rename", name = name.ToUpperInvariant(), new_name = renamed });
        rename.PendingAction!.Preview.Phrase.ShouldBe(name);
        await ConfirmTypedAsync(superAdmin, rename);

        var (archive, _) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "archive", name = renamed });
        await ConfirmTypedAsync(superAdmin, archive);
        var (restore, _) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "restore", name = renamed });
        await ConfirmAsync(superAdmin, restore);
        var after = (await OkAsync<List<AccountLabelResponse>>(Client.GetAsync("/api/account-labels?includeArchived=true", superAdmin.AccessToken))).Single(l => l.Id == label.Id);
        (after.Name, after.Archived).ShouldBe((renamed, false));

        // restoring one that isn't archived is refused on the card
        var (againCard, again) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "restore", name = renamed });
        againCard.PendingAction.ShouldBeNull();
        again["message"]!.GetValue<string>().ShouldContain("isn't archived");

        // nothing is filed under it, so it can be deleted, with its name typed
        var (delete, _) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "delete", name = renamed });
        delete.PendingAction!.Preview.Phrase.ShouldBe(renamed);
        await ConfirmTypedAsync(superAdmin, delete);
        (await OkAsync<List<AccountLabelResponse>>(Client.GetAsync("/api/account-labels?includeArchived=true", superAdmin.AccessToken)))
            .ShouldNotContain(l => l.Id == label.Id);
    }

    [Fact]
    public async Task A_label_payments_are_filed_under_is_not_deleted_by_proton()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = "Label " + Guid.NewGuid().ToString("N")[..8];
        var label = await OkAsync<AccountLabelResponse>(Client.PostJsonAsync("/api/account-labels", new { name }, superAdmin.AccessToken));
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await OkAsync<JsonNode>(Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 50m, currency = "USD", accountLabelId = label.Id }, superAdmin.AccessToken));

        var (card, result) = await ProposeAsync(superAdmin, "propose_account_label", new { step = "delete", name });
        card.PendingAction.ShouldBeNull();
        result["message"]!.GetValue<string>().ShouldContain("archive it instead");
    }

    [Fact]
    public async Task An_excuse_is_taken_off_with_the_persons_name_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var month = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).ToString("yyyy-MM");
        // excuses are read from the person's salary for the month, as the Salaries page shows them
        await _steps.SetJoinedOnAsync(designer.User.Id, "2026-01-01");
        await OkAsync<JsonNode>(Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1760m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken));
        var excuse = await OkAsync<ID_Indetails.api.DTO.Response.Payroll.HourExcuseResponse>(
            Client.PostJsonAsync($"/api/employees/{designer.User.Id}/hour-excuses", new { month, kind = "minutes", minutes = 90 }, superAdmin.AccessToken));

        var (card, result) = await ProposeAsync(superAdmin, "propose_hour_excuse", new { user_id = designer.User.Id, month, remove_excuse_id = excuse.Id });
        card.PendingAction.ShouldNotBeNull(result.ToJsonString());
        card.PendingAction.Preview.Confirm.ShouldBe(ProtonConfirm.Typed);
        await ConfirmTypedAsync(superAdmin, card);
        (await Client.DeleteAsync($"/api/hour-excuses/{excuse.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Proton_budget_is_topped_up_only_with_an_amount_the_user_typed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var before = await OkAsync<ProtonBudgetResponse>(Client.GetAsync("/api/proton/budget", superAdmin.AccessToken));

        var (untypedCard, untyped) = await ProposeAsync(superAdmin, "propose_proton_top_up", new { amount_usd = 3m }, "Give Proton some more budget");
        untypedCard.PendingAction.ShouldBeNull();
        untyped["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();

        var (card, _) = await ProposeAsync(superAdmin, "propose_proton_top_up", new { amount_usd = 3m, note = "Busy month" }, "Add 3 dollars to Proton's budget");
        card.PendingAction!.Preview.Phrase.ShouldBe("3");
        await ConfirmAsync(superAdmin, card, "3");
        (await OkAsync<ProtonBudgetResponse>(Client.GetAsync("/api/proton/budget", superAdmin.AccessToken))).TopUpsUsd.ShouldBeGreaterThanOrEqualTo(before.TopUpsUsd + 3m);
    }

    [Fact]
    public async Task A_person_changes_their_own_proton_suggestions_push_settings_and_phones()
    {
        var (email, password, superAdmin) = await _steps.ActiveSuperAdminAsync();

        var now = (await OkAsync<CurrentUserResponse>(Client.GetAsync("/api/auth/me", superAdmin.AccessToken))).ProtonReplySuggestions;
        var (suggestions, _) = await ProposeAsync(superAdmin, "propose_my_proton_suggestions", new { enabled = !now });
        await ConfirmAsync(superAdmin, suggestions);
        (await OkAsync<CurrentUserResponse>(Client.GetAsync("/api/auth/me", superAdmin.AccessToken))).ProtonReplySuggestions.ShouldBe(!now);

        var (push, _) = await ProposeAsync(superAdmin, "propose_notification_settings", new { mute = new[] { "money" } });
        await ConfirmAsync(superAdmin, push);
        (await OkAsync<NotificationSettingsResponse>(Client.GetAsync("/api/me/notification-settings", superAdmin.AccessToken))).MutedCategories
            .ShouldBe([NotificationCategory.Money]);
        var (nothing, nothingToDo) = await ProposeAsync(superAdmin, "propose_notification_settings", new { mute = new[] { "money" } });
        nothing.PendingAction.ShouldBeNull();
        nothingToDo["message"]!.GetValue<string>().ShouldContain("Nothing would change");

        // a phone signed in elsewhere is removed from the web
        var phone = await OkAsync<MobileAuthResponse>(Client.PostJsonAsync("/api/mobile/auth/login", new { email, password }));
        await OkAsync<DeviceResponse>(Client.PutJsonAsync("/api/me/devices",
            new { pushToken = "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]", platform = "android", appVersion = "1.0.0" }, phone.AccessToken));
        var device = (await OkAsync<List<DeviceResponse>>(Client.GetAsync("/api/me/devices", superAdmin.AccessToken))).Single(d => !d.Current);
        var (remove, _) = await ProposeAsync(superAdmin, "propose_remove_device", new { device_id = device.Id });
        await ConfirmAsync(superAdmin, remove);
        (await OkAsync<List<DeviceResponse>>(Client.GetAsync("/api/me/devices", superAdmin.AccessToken))).ShouldNotContain(d => d.Id == device.Id);

        // no photo to remove
        var (photoCard, photo) = await ProposeAsync(superAdmin, "propose_remove_my_photo", new { });
        photoCard.PendingAction.ShouldBeNull();
        photo["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }
}
