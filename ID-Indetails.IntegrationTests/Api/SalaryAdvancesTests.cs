using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Salary advances: people with pay ask, super admins (or whoever holds salary_advance:manage) approve, decline or record
/// them, and each month's repayment comes off what the person is handed. The clock is real, so every advance is planned
/// from the current month; the tests never assert what a month took, which depends on the day they run. Follow your own rows.
/// </summary>
public class SalaryAdvancesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Mine = "/api/me/salary-advances";
    private const string Company = "/api/company/salary-advances";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static DateOnly Today => WorkEntryTestSteps.CompanyDay(DateTime.UtcNow);

    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);

    private static object[] Plan(params (DateOnly Month, decimal Amount)[] months)
        => months.Select(m => (object)new { month = m.Month, amount = m.Amount }).ToArray();

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)))
            .Items.Where(n => n.Kind == kind).ToList();

    /// <summary>Someone who joined on 1 January 2026 on a monthly salary (1,760 USD by default), so the current month is a whole month.</summary>
    private async Task<AuthResponse> PaidAsync(AuthResponse superAdmin, string domain = DomainCodes.Design, decimal salary = 1760m)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, domain);
        await _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");
        var paid = await Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
            new { basis = "monthly", amount = salary, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken);
        paid.IsSuccessStatusCode.ShouldBeTrue(await paid.Content.ReadAsStringAsync());
        return person;
    }

    /// <summary>An admin of <paramref name="domain"/> who has been granted the permission to give advances.</summary>
    private async Task<AuthResponse> AdvanceAdminAsync(AuthResponse superAdmin, string domain)
    {
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, domain);
        var granted = await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.SalaryAdvanceManage, action = "grant", reason = "Handles advances" } } },
            superAdmin.AccessToken);
        granted.IsSuccessStatusCode.ShouldBeTrue(await granted.Content.ReadAsStringAsync());
        return admin;
    }

    private Task<MySalaryAdvanceResponse> AskAsync(AuthResponse person, decimal amount = 400m, string reason = "Rent is due")
        => CreatedAsync<MySalaryAdvanceResponse>(Client.PostJsonAsync(Mine, new { amount, reason }, person.AccessToken));

    /// <summary>Records an advance that comes off one month: the current one unless said.</summary>
    private Task<SalaryAdvanceResponse> RecordAsync(AuthResponse by, AuthResponse person, decimal amount = 500m, DateOnly? month = null)
        => CreatedAsync<SalaryAdvanceResponse>(Client.PostJsonAsync(
            Company, new { userId = person.User.Id, amount, months = Plan((month ?? ThisMonth, amount)), note = "Handed over" }, by.AccessToken));

    private Task<HttpResponseMessage> ApproveCallAsync(AuthResponse by, long id, object body)
        => Client.PostJsonAsync($"{Company}/{id}/approve", body, by.AccessToken);

    private Task<MySalaryAdvancesResponse> MineOfAsync(AuthResponse person)
        => OkAsync<MySalaryAdvancesResponse>(Client.GetAsync(Mine, person.AccessToken));

    // ----- asking -----

    [Fact]
    public async Task An_employee_asks_sees_the_request_and_takes_it_back_and_nobody_else_sees_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var employee = await PaidAsync(superAdmin);
        var colleague = await PaidAsync(superAdmin);

        var before = await MineOfAsync(employee);
        before.CanAsk.ShouldBeTrue();
        before.CannotAskBecause.ShouldBeNull();
        before.Currency.ShouldBe("USD");
        before.Items.ShouldBeEmpty();

        var asked = await AskAsync(employee, 400m, "Rent is due");
        asked.Status.ShouldBe(SalaryAdvanceStatus.Pending);
        asked.Amount.ShouldBe(400m);
        asked.AskedAmount.ShouldBe(400m);
        asked.Currency.ShouldBe("USD");
        asked.Reason.ShouldBe("Rent is due");
        asked.Months.ShouldBeEmpty();
        asked.Left.ShouldBeNull();

        // one request at a time
        var waiting = await MineOfAsync(employee);
        waiting.CanAsk.ShouldBeFalse();
        waiting.CannotAskBecause.ShouldBe(ErrorCodes.SalaryAdvancePending);
        waiting.Items.ShouldHaveSingleItem().Id.ShouldBe(asked.Id);
        await (await Client.PostJsonAsync(Mine, new { amount = 100m, reason = "Again" }, employee.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvancePending);

        // a colleague never sees it: it reads as missing, and the company's pages are closed to them
        (await MineOfAsync(colleague)).Items.ShouldBeEmpty();
        await (await Client.PostEmptyAsync($"{Mine}/{asked.Id}/withdraw", colleague.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"{Company}/{asked.Id}", colleague.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        // the super admins are told by name, never the amount
        var requested = (await OfKindAsync(superAdmin, NotificationKinds.SalaryAdvanceRequested)).Single(n => n.SubjectId == asked.Id);
        requested.SubjectType.ShouldBe("salary_advance");
        requested.Payload!.Value.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        requested.Payload.Value.GetRawText().ShouldNotContain("amount");
        (await OfKindAsync(employee, NotificationKinds.SalaryAdvanceRequested)).ShouldNotContain(n => n.SubjectId == asked.Id);

        var withdrawn = await OkAsync<MySalaryAdvanceResponse>(Client.PostEmptyAsync($"{Mine}/{asked.Id}/withdraw", employee.AccessToken));
        withdrawn.Status.ShouldBe(SalaryAdvanceStatus.Withdrawn);
        (await OfKindAsync(superAdmin, NotificationKinds.SalaryAdvanceWithdrawn)).ShouldContain(n => n.SubjectId == asked.Id);
        await (await Client.PostEmptyAsync($"{Mine}/{asked.Id}/withdraw", employee.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceDecided);

        var after = await MineOfAsync(employee);
        after.CanAsk.ShouldBeTrue();
        after.Items.ShouldHaveSingleItem().Status.ShouldBe(SalaryAdvanceStatus.Withdrawn);
    }

    [Fact]
    public async Task Someone_with_no_pay_set_cannot_ask_and_a_request_needs_an_amount_and_a_reason()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, unpaid) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var mine = await MineOfAsync(unpaid);
        mine.CanAsk.ShouldBeFalse();
        mine.CannotAskBecause.ShouldBe(ErrorCodes.SalaryAdvanceNoPay);
        mine.Currency.ShouldBeNull();
        await (await Client.PostJsonAsync(Mine, new { amount = 100m, reason = "Rent" }, unpaid.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceNoPay);

        var paid = await PaidAsync(superAdmin);
        await (await Client.PostJsonAsync(Mine, new { amount = 0m, reason = "Rent" }, paid.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync(Mine, new { amount = 100m, reason = " " }, paid.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync(Mine, new { amount = 100m, reason = new string('x', 501) }, paid.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        ((int)(await Client.GetAsync(Mine)).StatusCode).ShouldBe(401);
        (await MineOfAsync(paid)).Items.ShouldBeEmpty();
    }

    // ----- deciding -----

    [Fact]
    public async Task A_super_admin_approves_with_months_the_person_is_told_and_a_new_advance_waits_until_it_is_paid_back()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var employee = await PaidAsync(superAdmin);
        var asked = await AskAsync(employee, 600m, "School fees");

        var pending = await OkAsync<PagedResponse<SalaryAdvanceResponse>>(Client.GetAsync($"{Company}?view=pending&pageSize=100", superAdmin.AccessToken));
        var row = pending.Items.Single(r => r.Id == asked.Id);
        row.Person.Id.ShouldBe(employee.User.Id);
        row.Reason.ShouldBe("School fees");
        row.AskedAmount.ShouldBe(600m);
        row.CanDecide.ShouldBeTrue();

        var next = ThisMonth.AddMonths(1);
        var approved = await OkAsync<SalaryAdvanceResponse>(ApproveCallAsync(
            superAdmin, asked.Id, new { months = Plan((ThisMonth, 300m), (next, 300m)), note = "Back by the end of next month" }));
        approved.Status.ShouldBe(SalaryAdvanceStatus.Approved);
        approved.Amount.ShouldBe(600m);
        approved.GivenOn.ShouldBe(Today);
        approved.DecidedByName.ShouldNotBeNullOrWhiteSpace();
        approved.DecisionNote.ShouldBe("Back by the end of next month");
        approved.Months.Select(m => m.Month).ShouldBe([ThisMonth, next]);
        approved.Months.Select(m => m.Planned).ShouldBe([300m, 300m]);
        approved.CanDecide.ShouldBeFalse();
        approved.CanCancel.ShouldBeTrue();
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth, 600m)) })).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceDecided);

        // the person is told, without an amount; they see the months and what is left, never who decided nor what came off each month
        var told = (await OfKindAsync(employee, NotificationKinds.SalaryAdvanceApproved)).Single(n => n.SubjectId == asked.Id);
        (told.Payload?.GetRawText() ?? "{}").ShouldNotContain("amount");
        var json = await (await Client.GetAsync(Mine, employee.AccessToken)).Content.ReadAsStringAsync();
        // no pay figure of theirs: the reason codes say "salary_advance_…", so look for the pay fields themselves
        foreach (var field in new[] { "decidedBy", "monthlyPay", "basePay", "hourlyRate", "toHandOver", "\"total\"" })
        {
            json.ShouldNotContain(field, Case.Insensitive);
        }
        var mine = await MineOfAsync(employee);
        var item = mine.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(SalaryAdvanceStatus.Approved);
        item.GivenOn.ShouldBe(Today);
        item.DecisionNote.ShouldBe("Back by the end of next month");
        item.Months.Count.ShouldBe(2);
        item.Months.ShouldAllBe(m => m.Taken == null);

        // the history keeps the step, at company level and without an amount
        const string approvedEvents = "SELECT COUNT(*) FROM activity_events WHERE kind = 'salary_advance_approved' AND subject_id = @u " +
            "AND domain_code IS NULL AND amount IS NULL AND prev_amount IS NULL";
        (await _steps.ScalarAsync<int>(approvedEvents, ("@u", employee.User.Id))).ShouldBe(1);
        var payload = await _steps.ScalarAsync<string>(
            "SELECT payload FROM activity_events WHERE kind = 'salary_advance_approved' AND subject_id = @u", ("@u", employee.User.Id));
        payload.ShouldNotContain("amount", Case.Insensitive);

        // one at a time: nothing new until this one is paid back
        mine.CanAsk.ShouldBeFalse();
        mine.CannotAskBecause.ShouldBe(ErrorCodes.SalaryAdvanceOpen);
        await (await Client.PostJsonAsync(Mine, new { amount = 100m, reason = "More" }, employee.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceOpen);
        await (await Client.PostJsonAsync(Company, new { userId = employee.User.Id, amount = 100m, months = Plan((ThisMonth, 100m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceOpen);

        var open = await OkAsync<PagedResponse<SalaryAdvanceResponse>>(Client.GetAsync($"{Company}?view=open&pageSize=100", superAdmin.AccessToken));
        open.Items.ShouldContain(r => r.Id == asked.Id);
    }

    [Fact]
    public async Task A_declined_request_tells_the_person_and_frees_them_to_ask_again()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var employee = await PaidAsync(superAdmin);
        var asked = await AskAsync(employee);

        var declined = await OkAsync<SalaryAdvanceResponse>(Client.PostJsonAsync($"{Company}/{asked.Id}/decline", new { note = "Not this month" }, superAdmin.AccessToken));
        declined.Status.ShouldBe(SalaryAdvanceStatus.Declined);
        declined.DecisionNote.ShouldBe("Not this month");
        declined.Months.ShouldBeEmpty();
        declined.Left.ShouldBeNull();
        (await OfKindAsync(employee, NotificationKinds.SalaryAdvanceDeclined)).ShouldContain(n => n.SubjectId == asked.Id);
        await (await Client.PostJsonAsync($"{Company}/{asked.Id}/decline", new { }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceDecided);
        await (await Client.PostEmptyAsync($"{Mine}/{asked.Id}/withdraw", employee.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceDecided);

        var mine = await MineOfAsync(employee);
        mine.CanAsk.ShouldBeTrue();
        mine.Items.ShouldHaveSingleItem().DecisionNote.ShouldBe("Not this month");
        (await AskAsync(employee)).Status.ShouldBe(SalaryAdvanceStatus.Pending);
    }

    [Fact]
    public async Task Approving_checks_what_was_asked_a_months_pay_and_that_the_months_add_up()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var employee = await PaidAsync(superAdmin);
        var asked = await AskAsync(employee, 500m);

        // more than asked
        await (await ApproveCallAsync(superAdmin, asked.Id, new { amount = 600m, months = Plan((ThisMonth, 600m)) }))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMoreThanAsked);
        // months that don't add up to what is given
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth, 400m)) }))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMonthsTotal);
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth, 300m), (ThisMonth.AddMonths(1), 300m)) }))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMonthsTotal);
        // months before this one, or more than a year away
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth.AddMonths(-1), 500m)) }))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMonthsInvalid);
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth.AddMonths(12), 500m)) }))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMonthsInvalid);
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth, 250m), (ThisMonth, 250m)) }))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMonthsInvalid);
        // no months at all
        await (await ApproveCallAsync(superAdmin, asked.Id, new { months = Plan() })).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // nothing above changed the request: it is still waiting, and approving it as asked works
        var approved = await OkAsync<SalaryAdvanceResponse>(ApproveCallAsync(superAdmin, asked.Id, new { months = Plan((ThisMonth, 500m)) }));
        approved.Status.ShouldBe(SalaryAdvanceStatus.Approved);

        // asking has no limit (the form never knows the pay), giving does: a month's pay at most, in total and in each month
        var other = await PaidAsync(superAdmin);
        var big = await AskAsync(other, 2000m);
        await (await ApproveCallAsync(superAdmin, big.Id, new { months = Plan((ThisMonth, 2000m)) })).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceOverPay);
        await (await ApproveCallAsync(superAdmin, big.Id, new { amount = 1000m, months = Plan((ThisMonth, 1800m)) }))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceOverPay);
        var lowered = await OkAsync<SalaryAdvanceResponse>(ApproveCallAsync(superAdmin, big.Id, new { amount = 1500m, months = Plan((ThisMonth, 1500m)) }));
        lowered.Amount.ShouldBe(1500m);
        lowered.AskedAmount.ShouldBe(2000m);
    }

    // ----- who may -----

    [Fact]
    public async Task Only_those_who_hold_the_permission_decide_an_admin_only_in_their_domain_and_nobody_their_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var designEmployee = await PaidAsync(superAdmin);
        var siteWorker = await PaidAsync(superAdmin, DomainCodes.Execution);
        var designAdmin = await AdvanceAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, plainAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var asked = await AskAsync(designEmployee);

        // the holders who see the person's domain are told of the request, and nobody else
        (await OfKindAsync(designAdmin, NotificationKinds.SalaryAdvanceRequested)).ShouldContain(n => n.SubjectId == asked.Id);
        (await OfKindAsync(plainAdmin, NotificationKinds.SalaryAdvanceRequested)).ShouldNotContain(n => n.SubjectId == asked.Id);
        (await OfKindAsync(superAdmin, NotificationKinds.SalaryAdvanceRequested)).ShouldContain(n => n.SubjectId == asked.Id);

        // without salary_advance:manage everything is refused
        foreach (var someone in new[] { designEmployee, plainAdmin })
        {
            await (await Client.GetAsync(Company, someone.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await ApproveCallAsync(someone, asked.Id, new { months = Plan((ThisMonth, 400m)) })).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync($"{Company}/{asked.Id}/decline", new { }, someone.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostJsonAsync(Company, new { userId = designEmployee.User.Id, amount = 100m, months = Plan((ThisMonth, 100m)) }, someone.AccessToken))
                .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }

        // holding it, an admin decides for their own domain
        var approved = await OkAsync<SalaryAdvanceResponse>(ApproveCallAsync(designAdmin, asked.Id, new { months = Plan((ThisMonth, 400m)) }));
        approved.Status.ShouldBe(SalaryAdvanceStatus.Approved);

        // another domain's people don't exist for them
        var siteAdvance = await RecordAsync(superAdmin, siteWorker, 300m);
        await (await Client.GetAsync($"{Company}/{siteAdvance.Id}", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"{Company}/{siteAdvance.Id}/settle", new { kind = "cash" }, designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync(Company, new { userId = siteWorker.User.Id, amount = 100m, months = Plan((ThisMonth, 100m)) }, designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var open = await OkAsync<PagedResponse<SalaryAdvanceResponse>>(Client.GetAsync($"{Company}?view=open&pageSize=100", designAdmin.AccessToken));
        open.Items.ShouldContain(r => r.Id == asked.Id);
        open.Items.ShouldNotContain(r => r.Id == siteAdvance.Id);

        // nobody gives or decides their own
        await (await Client.PostJsonAsync(Company, new { userId = designAdmin.User.Id, amount = 100m, months = Plan((ThisMonth, 100m)) }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        var own = await AskAsync(designAdmin, 300m);
        await (await ApproveCallAsync(designAdmin, own.Id, new { months = Plan((ThisMonth, 300m)) })).ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);
        await (await Client.PostJsonAsync($"{Company}/{own.Id}/decline", new { }, designAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.EmployeeChangeNotAllowed);

        // someone else does
        (await OkAsync<SalaryAdvanceResponse>(ApproveCallAsync(superAdmin, own.Id, new { months = Plan((ThisMonth, 300m)) })))
            .Status.ShouldBe(SalaryAdvanceStatus.Approved);
    }

    // ----- recording, changing, cancelling -----

    [Fact]
    public async Task A_recorded_advance_has_its_months_changed_and_is_cancelled_in_the_month_it_was_given()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await PaidAsync(superAdmin);

        // the day given is from the first of this month up to today, and the amount is at most a month's pay
        await (await Client.PostJsonAsync(Company,
                new { userId = person.User.Id, amount = 500m, givenOn = Today.AddDays(1), months = Plan((ThisMonth, 500m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceGivenOn);
        await (await Client.PostJsonAsync(Company,
                new { userId = person.User.Id, amount = 500m, givenOn = ThisMonth.AddDays(-1), months = Plan((ThisMonth, 500m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceGivenOn);
        await (await Client.PostJsonAsync(Company, new { userId = person.User.Id, amount = 2000m, months = Plan((ThisMonth, 2000m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceOverPay);
        await (await Client.PostJsonAsync(Company, new { userId = long.MaxValue, amount = 500m, months = Plan((ThisMonth, 500m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        var recorded = await RecordAsync(superAdmin, person, 500m);
        recorded.Status.ShouldBe(SalaryAdvanceStatus.Approved);
        recorded.AskedAmount.ShouldBeNull();
        recorded.Reason.ShouldBeNull();
        recorded.RecordedByName.ShouldNotBeNullOrWhiteSpace();
        recorded.DecidedByName.ShouldBeNull();
        recorded.GivenOn.ShouldBe(Today);
        recorded.Person.Id.ShouldBe(person.User.Id);
        recorded.CanChange.ShouldBeTrue();
        recorded.CanCancel.ShouldBeTrue();
        (await OfKindAsync(person, NotificationKinds.SalaryAdvanceRecorded)).ShouldContain(n => n.SubjectId == recorded.Id);
        await (await Client.PostJsonAsync(Company, new { userId = person.User.Id, amount = 100m, months = Plan((ThisMonth, 100m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceOpen);

        // the months can be changed while they add up to the advance
        var next = ThisMonth.AddMonths(1);
        await (await Client.PutJsonAsync($"{Company}/{recorded.Id}/months", new { months = Plan((ThisMonth, 100m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryAdvanceMonthsTotal);
        var changed = await OkAsync<SalaryAdvanceResponse>(
            Client.PutJsonAsync($"{Company}/{recorded.Id}/months", new { months = Plan((ThisMonth, 200m), (next, 300m)) }, superAdmin.AccessToken));
        changed.Months.Select(m => m.Month).ShouldBe([ThisMonth, next]);
        changed.Months.Select(m => m.Planned).ShouldBe([200m, 300m]);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'salary_advance_months_changed' AND subject_id = @u", ("@u", person.User.Id))).ShouldBe(1);

        // given by mistake: cancelled while its month is open
        var cancelled = await OkAsync<SalaryAdvanceResponse>(
            Client.PostJsonAsync($"{Company}/{recorded.Id}/cancel", new { note = "Given by mistake" }, superAdmin.AccessToken));
        cancelled.Status.ShouldBe(SalaryAdvanceStatus.Cancelled);
        cancelled.CancelledAt.ShouldNotBeNull();
        cancelled.Left.ShouldBeNull();
        cancelled.CanChange.ShouldBeFalse();
        cancelled.CanCancel.ShouldBeFalse();
        await (await Client.PostJsonAsync($"{Company}/{recorded.Id}/cancel", new { }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceClosed);
        await (await Client.PutJsonAsync($"{Company}/{recorded.Id}/months", new { months = Plan((ThisMonth, 500m)) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceClosed);
        await (await Client.PostJsonAsync($"{Company}/{recorded.Id}/settle", new { kind = "cash" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceClosed);

        // nothing is left to pay back, so they can ask again and be given another
        var mine = await MineOfAsync(person);
        mine.CanAsk.ShouldBeTrue();
        mine.Items.ShouldHaveSingleItem().Status.ShouldBe(SalaryAdvanceStatus.Cancelled);
        (await RecordAsync(superAdmin, person, 100m)).Status.ShouldBe(SalaryAdvanceStatus.Approved);
    }

    [Fact]
    public async Task An_advance_given_in_an_earlier_month_can_no_longer_be_cancelled()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await PaidAsync(superAdmin);
        var recorded = await RecordAsync(superAdmin, person, 300m);
        await _steps.ExecuteAsync(
            "UPDATE salary_advances SET given_on = @day WHERE id = @id", ("@day", ThisMonth.AddDays(-5).ToString("yyyy-MM-dd")), ("@id", recorded.Id));

        await (await Client.PostJsonAsync($"{Company}/{recorded.Id}/cancel", new { }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceCancelClosed);
        var shown = await OkAsync<SalaryAdvanceResponse>(Client.GetAsync($"{Company}/{recorded.Id}", superAdmin.AccessToken));
        shown.Status.ShouldBe(SalaryAdvanceStatus.Approved);
        shown.CanCancel.ShouldBeFalse();
        shown.CanChange.ShouldBeTrue();
    }

    // ----- settling -----

    [Fact]
    public async Task What_is_left_is_settled_as_repaid_in_cash_or_written_off_and_the_person_can_ask_again()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var cashPerson = await PaidAsync(superAdmin);
        var writtenOffPerson = await PaidAsync(superAdmin);
        // planned for next month, so this month takes nothing and all of it is left to settle
        var cashAdvance = await RecordAsync(superAdmin, cashPerson, 500m, ThisMonth.AddMonths(1));
        var writtenOffAdvance = await RecordAsync(superAdmin, writtenOffPerson, 400m, ThisMonth.AddMonths(1));

        var cash = await OkAsync<SalaryAdvanceResponse>(
            Client.PostJsonAsync($"{Company}/{cashAdvance.Id}/settle", new { kind = "cash", note = "Paid by hand" }, superAdmin.AccessToken));
        cash.Status.ShouldBe(SalaryAdvanceStatus.Approved);
        var settlement = cash.Settlement.ShouldNotBeNull();
        settlement.Kind.ShouldBe(SalaryAdvanceSettlement.Cash);
        settlement.Amount.ShouldBe(500m);
        settlement.SettledOn.ShouldBe(Today);
        settlement.SettledByName.ShouldNotBeNullOrWhiteSpace();
        settlement.Note.ShouldBe("Paid by hand");
        cash.Left.ShouldBe(0m);
        cash.Unsettled.ShouldBeFalse();
        cash.CanChange.ShouldBeFalse();
        cash.CanCancel.ShouldBeFalse();
        await (await Client.PostJsonAsync($"{Company}/{cashAdvance.Id}/settle", new { kind = "cash" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceClosed);
        await (await Client.PostJsonAsync($"{Company}/{cashAdvance.Id}/cancel", new { }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.SalaryAdvanceClosed);

        var writtenOff = await OkAsync<SalaryAdvanceResponse>(
            Client.PostJsonAsync($"{Company}/{writtenOffAdvance.Id}/settle", new { kind = "written_off" }, superAdmin.AccessToken));
        writtenOff.Settlement.ShouldNotBeNull().Kind.ShouldBe(SalaryAdvanceSettlement.WrittenOff);
        writtenOff.Settlement!.Amount.ShouldBe(400m);
        writtenOff.Left.ShouldBe(0m);
        (await _steps.ScalarAsync<string>("SELECT settled_kind FROM salary_advances WHERE id = @id", ("@id", writtenOffAdvance.Id))).ShouldBe("written_off");

        // both are closed now, and their people are free to ask again
        var closed = await OkAsync<PagedResponse<SalaryAdvanceResponse>>(Client.GetAsync($"{Company}?view=closed&pageSize=100", superAdmin.AccessToken));
        closed.Items.ShouldContain(r => r.Id == cashAdvance.Id);
        closed.Items.ShouldContain(r => r.Id == writtenOffAdvance.Id);
        var open = await OkAsync<PagedResponse<SalaryAdvanceResponse>>(Client.GetAsync($"{Company}?view=open&pageSize=100", superAdmin.AccessToken));
        open.Items.ShouldNotContain(r => r.Id == cashAdvance.Id || r.Id == writtenOffAdvance.Id);

        var mine = await MineOfAsync(cashPerson);
        mine.CanAsk.ShouldBeTrue();
        var item = mine.Items.ShouldHaveSingleItem();
        item.SettledKind.ShouldBe(SalaryAdvanceSettlement.Cash);
        item.SettledOn.ShouldBe(Today);
        item.Left.ShouldBe(0m);
        (await AskAsync(cashPerson)).Status.ShouldBe(SalaryAdvanceStatus.Pending);
    }

    // ----- leaving -----

    [Fact]
    public async Task Deactivating_someone_with_an_advance_still_to_pay_back_tells_the_other_super_admins()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, otherSuperAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.SuperAdmin, null);
        var leaver = await PaidAsync(superAdmin);
        var clean = await PaidAsync(superAdmin);
        var advance = await RecordAsync(superAdmin, leaver, 500m);

        foreach (var person in new[] { leaver, clean })
        {
            var left = await Client.PostEmptyAsync($"/api/employees/{person.User.Id}/deactivate", superAdmin.AccessToken);
            left.IsSuccessStatusCode.ShouldBeTrue(await left.Content.ReadAsStringAsync());
        }

        // the other super admin hears about the one with an advance, not the other; whoever deactivated isn't told of their own action
        var unsettled = await OfKindAsync(otherSuperAdmin, NotificationKinds.SalaryAdvanceUnsettled);
        var note = unsettled.Where(n => n.SubjectId == leaver.User.Id).ShouldHaveSingleItem();
        note.SubjectType.ShouldBe("user");
        note.Payload!.Value.GetProperty("userId").GetInt64().ShouldBe(leaver.User.Id);
        note.Payload.Value.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        note.Payload.Value.GetRawText().ShouldNotContain("amount");
        unsettled.ShouldNotContain(n => n.SubjectId == clean.User.Id);
        (await OfKindAsync(superAdmin, NotificationKinds.SalaryAdvanceUnsettled)).ShouldNotContain(n => n.SubjectId == leaver.User.Id);

        // the advance now asks to be settled
        var shown = await OkAsync<SalaryAdvanceResponse>(Client.GetAsync($"{Company}/{advance.Id}", superAdmin.AccessToken));
        shown.Person.LeftOn.ShouldBe(Today);
        shown.Unsettled.ShouldBeTrue();
        var settled = await OkAsync<SalaryAdvanceResponse>(
            Client.PostJsonAsync($"{Company}/{advance.Id}/settle", new { kind = "written_off", note = "They left" }, superAdmin.AccessToken));
        settled.Unsettled.ShouldBeFalse();
        settled.Settlement.ShouldNotBeNull().Kind.ShouldBe(SalaryAdvanceSettlement.WrittenOff);
    }
}
