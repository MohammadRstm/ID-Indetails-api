using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectDepartmentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    /// <summary>A Design client project of 10,000 before TVA, with <paramref name="departmentIds"/>, owned by <paramref name="ownerId"/>.</summary>
    private async Task<ProjectResponse> ProjectAsync(AuthResponse superAdmin, IReadOnlyList<long> departmentIds, long? ownerId = null)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 10000m,
            tvaRate = 0m,
            ownerId,
            departmentIds,
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    private async Task<ProjectDepartmentsResponse> DepartmentsAsync(AuthResponse caller, long projectId)
        => await (await Client.GetAsync($"/api/projects/{projectId}/departments", caller.AccessToken)).ReadAsync<ProjectDepartmentsResponse>();

    private Task<long> NotificationsAsync(long recipientId, string kind, long projectId)
        => _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = @k AND project_id = @p",
            ("@r", recipientId), ("@k", kind), ("@p", projectId));

    [Fact]
    public async Task Departments_split_the_price_equally_and_whoever_writes_the_money_reshapes_it_with_a_reason()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure");
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await ProjectAsync(superAdmin, [architecture, structure.Id]);
        project.Departments!.Select(d => d.Id).ShouldBe([architecture, structure.Id], ignoreOrder: true);

        var split = await DepartmentsAsync(superAdmin, project.Id);
        split.Departments.Select(d => d.Share).ShouldBe([0.5m, 0.5m]);
        split.Departments.ShouldAllBe(d => d.Money!.Budget == 5000m && d.Money.Left == 5000m);

        // an admin who doesn't see the money sees who works on it, nothing more, and doesn't set the shares
        var plain = await DepartmentsAsync(admin, project.Id);
        plain.Departments.ShouldAllBe(d => d.Share == null && d.Money == null);
        var shares = new
        {
            shares = new[] { new { departmentId = architecture, share = 0.7m }, new { departmentId = structure.Id, share = 0.3m } },
            reason = "Structure is lighter than planned",
        };
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments/shares", shares, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments/shares", new
            {
                shares = new[] { new { departmentId = architecture, share = 0.7m }, new { departmentId = structure.Id, share = 0.2m } },
                reason = "Typo",
            }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var reshaped = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments/shares", shares, superAdmin.AccessToken))
            .ReadAsync<ProjectDepartmentsResponse>();
        reshaped.Departments.Single(d => d.Id == architecture).Money!.Budget.ShouldBe(7000m);
        reshaped.Departments.Single(d => d.Id == structure.Id).Money!.Budget.ShouldBe(3000m);

        // the reason is optional
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments/shares", new
            {
                shares = new[] { new { departmentId = architecture, share = 0.6m }, new { departmentId = structure.Id, share = 0.4m } },
            }, superAdmin.AccessToken))
            .ReadAsync<ProjectDepartmentsResponse>();

        var history = await (await Client.GetAsync($"/api/projects/{project.Id}/departments/history", superAdmin.AccessToken))
            .ReadAsync<List<ProjectDepartmentChangeResponse>>();
        history.Select(h => h.Kind).ShouldBe([ProjectDepartmentChangeKind.Shares, ProjectDepartmentChangeKind.Shares, ProjectDepartmentChangeKind.Created]);
        history[0].Reason.ShouldBeNull();
        history[1].Reason.ShouldBe("Structure is lighter than planned");
        history[1].Before.Select(b => b.Share).ShouldBe([0.5m, 0.5m]);
        history[1].After.Single(a => a.Id == structure.Id).Share.ShouldBe(0.3m);

        // adding a department starts the split again, equal
        var interior = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Interior");
        var three = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments",
                new { departmentIds = new[] { architecture, structure.Id, interior.Id } }, admin.AccessToken))
            .ReadAsync<ProjectDepartmentsResponse>();
        three.Departments.Count.ShouldBe(3);
        (await DepartmentsAsync(superAdmin, project.Id)).Departments.Select(d => d.Share).ShouldBe([0.3334m, 0.3333m, 0.3333m], ignoreOrder: true);

        // an Execution site has none
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await (await Client.GetAsync($"/api/projects/{site.Id}/departments", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task Hours_from_a_department_not_on_the_project_wait_until_it_is_added()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure");
        var (_, _, owner) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, engineer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.PutInTeamAsync(engineer.User.Id, structure.Id);
        var project = await ProjectAsync(superAdmin, [architecture], owner.User.Id);
        var start = WorkEntryTestSteps.HoursAgoOnOneDay(4, 45);

        // kept, not logged: the owner and the super admins are told
        var refused = await (await Work.TryLogAsync(engineer.AccessToken, project.Id, start, 45))
            .ShouldFailWithAsync(422, ErrorCodes.DepartmentNotOnProject);
        refused.Message.ShouldContain(structure.Name);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM work_entries WHERE user_id = @u", ("@u", engineer.User.Id))).ShouldBe(0);
        (await _steps.ScalarAsync<string>(
                "SELECT status FROM blocked_changes WHERE project_id = @p AND kind = 'work_entry_department'", ("@p", project.Id)))
            .ShouldBe("pending");
        (await NotificationsAsync(owner.User.Id, NotificationKinds.LogDepartmentBlocked, project.Id)).ShouldBe(1);
        (await NotificationsAsync(superAdmin.User.Id, NotificationKinds.LogDepartmentBlocked, project.Id)).ShouldBe(1);
        // they go through on their own, never by forcing
        var waitingId = await _steps.ScalarAsync<long>(
            "SELECT id FROM blocked_changes WHERE project_id = @p AND kind = 'work_entry_department'", ("@p", project.Id));
        await (await Client.PostEmptyAsync($"/api/blocked-changes/{waitingId}/force", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.DepartmentStillMissing);

        // the owner adds Structure: the hours go through, in Structure, and their logger hears it
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments",
                new { departmentIds = new[] { architecture, structure.Id } }, owner.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<long>(
                "SELECT team_id FROM work_entries WHERE user_id = @u AND project_id = @p", ("@u", engineer.User.Id), ("@p", project.Id)))
            .ShouldBe(structure.Id);
        (await _steps.ScalarAsync<string>("SELECT status FROM blocked_changes WHERE id = @id", ("@id", waitingId))).ShouldBe("forced");
        (await NotificationsAsync(engineer.User.Id, NotificationKinds.WorkEntryReleased, project.Id)).ShouldBe(1);

        var structureMoney = (await DepartmentsAsync(superAdmin, project.Id)).Departments.Single(d => d.Id == structure.Id).Money!;
        structureMoney.Minutes.ShouldBe(45);
        structureMoney.People.ShouldHaveSingleItem().Person.Id.ShouldBe(engineer.User.Id);

        // Structure has hours on it now, so it stays
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments", new { departmentIds = new[] { architecture } }, owner.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.DepartmentHasHours);
        // and, as a department of a project, can't be deleted
        await _steps.ExecuteAsync("UPDATE users SET team_id = @t WHERE id = @u", ("@t", architecture), ("@u", engineer.User.Id));
        await (await Client.DeleteAsync($"/api/teams/{structure.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.TeamOnProjects);
    }

    [Fact]
    public async Task Labour_with_no_exchange_rate_on_its_day_is_listed_in_its_currency_never_counted_as_0()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // 14,320,000 LBP a month is 89,500 LBP an hour in February 2026 (160 paid hours)
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 14_320_000m, currency = "LBP", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await ProjectAsync(superAdmin, [architecture]);

        // 3 hours in Architecture on Tuesday 10 February 2026 (09:00-12:00 in Beirut) and the paid hour of break: 4 hours,
        // 358,000 LBP, on a day with no LBP rate yet
        await _steps.ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, team_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, @t, 'Drawings', '2026-02-10 07:00:00', '2026-02-10 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id), ("@t", architecture));

        var money = (await DepartmentsAsync(superAdmin, project.Id)).Departments.Single(d => d.Id == architecture).Money!;
        money.Minutes.ShouldBe(180);
        money.Unconverted.ShouldBe([new CurrencyAmountResponse("LBP", 358_000m)]);
        money.Cost.ShouldBe(0m);
        money.Left.ShouldBe(money.Budget);
        var person = money.People.ShouldHaveSingleItem();
        person.Cost.ShouldBe(0m);
        person.Months.ShouldHaveSingleItem().HourlyRate.ShouldBeNull();

        // with a rate in force that day it is converted, 1 USD an hour, and no longer listed
        (await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_500m, effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var converted = (await DepartmentsAsync(superAdmin, project.Id)).Departments.Single(d => d.Id == architecture).Money!;
        converted.Unconverted.ShouldBeEmpty();
        converted.Cost.ShouldBe(4m);
        converted.Left.ShouldBe(converted.Budget - 4m);
        var month = converted.People.ShouldHaveSingleItem().Months.ShouldHaveSingleItem();
        month.HourlyRate.ShouldBe(1m);
        month.Cost.ShouldBe(4m);
    }

    [Fact]
    public async Task Someone_with_no_department_logs_only_on_a_project_with_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // from before departments were required
        await _steps.ExecuteAsync("UPDATE users SET team_id = NULL WHERE id = @u", ("@u", employee.User.Id));
        var withDepartments = await ProjectAsync(superAdmin, [await _steps.DesignDepartmentAsync()]);
        var without = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Work.TryLogAsync(employee.AccessToken, withDepartments.Id, WorkEntryTestSteps.HoursAgoOnOneDay(4, 30), 30))
            .ShouldFailWithAsync(422, ErrorCodes.DepartmentRequired);
        await Work.LogAsync(employee.AccessToken, without.Id, WorkEntryTestSteps.HoursAgoOnOneDay(4, 30), 30);
    }
}
