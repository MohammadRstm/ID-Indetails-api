using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class DesignVoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    /// <summary>A Design client project of <paramref name="price"/> before 11% TVA, with <paramref name="departmentIds"/>.</summary>
    private async Task<ProjectResponse> ProjectAsync(AuthResponse superAdmin, IReadOnlyList<long> departmentIds, decimal price = 10000m)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = price,
            tvaRate = 0.11m,
            departmentIds,
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    private async Task<DesignVoResponse> CreateAsync(AuthResponse who, long projectId, object body)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/design-vos", body, who.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<DesignVoResponse>();
    }

    private async Task<decimal> PriceAsync(AuthResponse who, long projectId)
        => (await (await Client.GetAsync($"/api/projects/{projectId}", who.AccessToken)).ReadAsync<ProjectResponse>()).Money!.Revenue;

    private async Task<IReadOnlyDictionary<long, decimal>> BudgetsAsync(AuthResponse who, long projectId)
        => (await (await Client.GetAsync($"/api/projects/{projectId}/departments", who.AccessToken)).ReadAsync<ProjectDepartmentsResponse>())
            .Departments.ToDictionary(d => d.Id, d => d.Money!.Budget);

    [Fact]
    public async Task A_variation_order_changes_the_price_and_each_department_keeps_its_part()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, "Structure")).Id;
        var project = await ProjectAsync(superAdmin, [architecture, structure]);

        // added at once, split by the shares: half each
        var guestHouse = await CreateAsync(superAdmin, project.Id, new
        {
            title = "Guest house",
            lines = new[] { new { description = "Guest house design", amount = 9000m }, new { description = "No pergola", amount = -1000m } },
            split = "shares",
            apply = true,
        });
        guestHouse.Status.ShouldBe(DesignVoStatus.Applied);
        guestHouse.Number.ShouldStartWith($"VO-{DateTime.UtcNow.Year}-");
        guestHouse.TotalUsd.ShouldBe(8000m);
        guestHouse.TvaUsd.ShouldBe(880m);
        guestHouse.Allocations.Select(a => a.Amount).ShouldBe([4000m, 4000m]);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(18000m);
        (await BudgetsAsync(superAdmin, project.Id)).Values.ShouldBe([9000m, 9000m]);

        // new shares move the price before VOs, never what a VO already gave
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments/shares", new
            {
                shares = new[] { new { departmentId = architecture, share = 0.7m }, new { departmentId = structure, share = 0.3m } },
                reason = "Structure is lighter",
            }, superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var budgets = await BudgetsAsync(superAdmin, project.Id);
        budgets[architecture].ShouldBe(11000m);
        budgets[structure].ShouldBe(7000m);

        // sent to the client, to Structure alone: nothing changes until it is confirmed
        var foundations = await CreateAsync(superAdmin, project.Id, new
        {
            title = "Deeper foundations",
            lines = new[] { new { description = "Soil study", amount = 2000m } },
            split = "departments",
            allocations = new[] { new { departmentId = structure, amount = 2000m } },
        });
        foundations.Status.ShouldBe(DesignVoStatus.Pending);
        foundations.SentAt.ShouldNotBeNull();
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(18000m);
        (await (await Client.PostEmptyAsync($"/api/design-vos/{foundations.Id}/confirm", superAdmin.AccessToken)).ReadAsync<DesignVoResponse>())
            .Status.ShouldBe(DesignVoStatus.Applied);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(20000m);
        (await BudgetsAsync(superAdmin, project.Id))[structure].ShouldBe(9000m);
        await (await Client.PostEmptyAsync($"/api/design-vos/{foundations.Id}/confirm", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.DesignVoNotPending);

        // each one is in the payment history, and Structure, with a VO's money, stays on the project
        (await _steps.ScalarAsync<long>(
                "SELECT COUNT(*) FROM project_payment_changes WHERE project_id = @p AND description LIKE 'VO-%'", ("@p", project.Id)))
            .ShouldBe(2);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments", new { departmentIds = new[] { architecture } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.DepartmentHasHours);

        // a pending one taken back
        var dropped = await CreateAsync(superAdmin, project.Id, new { title = "Pool", lines = new[] { new { description = "Pool", amount = 5000m } }, split = "shares" });
        (await (await Client.PostEmptyAsync($"/api/design-vos/{dropped.Id}/cancel", superAdmin.AccessToken)).ReadAsync<DesignVoResponse>())
            .Status.ShouldBe(DesignVoStatus.Cancelled);
        (await PriceAsync(superAdmin, project.Id)).ShouldBe(20000m);

        var list = await (await Client.GetAsync($"/api/projects/{project.Id}/design-vos", superAdmin.AccessToken)).ReadAsync<List<DesignVoResponse>>();
        list.Select(v => v.Id).ShouldBe([dropped.Id, foundations.Id, guestHouse.Id]);
    }

    [Fact]
    public async Task The_superadmin_leaves_tva_off_a_vo_or_puts_it_back_and_what_the_client_pays_follows()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var architecture = await _steps.DesignDepartmentAsync();
        var project = await ProjectAsync(superAdmin, [architecture]);
        async Task<ProjectMoneyResponse> MoneyAsync()
            => (await (await Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;

        // 10,000 with its 11% TVA, and 1,000 of extra visits with none
        var visits = await CreateAsync(superAdmin, project.Id, new
        {
            title = "Site visits",
            lines = new[] { new { description = "Two extra site visits", amount = 1000m } },
            split = "shares",
            apply = true,
            chargeTva = false,
        });
        (visits.TvaRate, visits.TvaUsd, visits.TotalWithTvaUsd).ShouldBe((0m, 0m, 1000m));
        (await MoneyAsync()).ShouldSatisfyAllConditions(
            m => m.Revenue.ShouldBe(11_000m),
            m => m.RevenueWithTva.ShouldBe(12_100m),
            m => m.PaymentWithTva.ShouldBe(12_100m));

        // a payment holds its share of the TVA there is: 1,100 of 12,100
        var paid = await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 6050m, currency = "USD" }, superAdmin.AccessToken);
        ((int)paid.StatusCode).ShouldBe(201, await paid.Content.ReadAsStringAsync());
        (await _steps.ScalarAsync<decimal>("SELECT tva_usd FROM payments WHERE project_id = @p", ("@p", project.Id))).ShouldBe(550m);

        // put back, the visits carry the TVA again
        async Task<DesignVoResponse> SetAsync(long voId, bool chargeTva)
        {
            var response = await Client.PutJsonAsync($"/api/design-vos/{voId}/tva", new { chargeTva }, superAdmin.AccessToken);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<DesignVoResponse>();
        }

        (await SetAsync(visits.Id, true)).ShouldSatisfyAllConditions(v => v.TvaRate.ShouldBe(0.11m), v => v.TotalWithTvaUsd.ShouldBe(1110m));
        (await MoneyAsync()).RevenueWithTva.ShouldBe(12_210m);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM activity_events WHERE kind = 'design_vo_tva_set' AND subject_id = @v", ("@v", visits.Id)))
            .ShouldBe(1);
        // asked again, nothing changes and nothing is written
        await SetAsync(visits.Id, true);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM activity_events WHERE kind = 'design_vo_tva_set' AND subject_id = @v", ("@v", visits.Id)))
            .ShouldBe(1);

        // a pending one changes too; a cancelled one stays as it was, and only whoever writes the money decides
        var pending = await CreateAsync(superAdmin, project.Id, new
        {
            title = "Pergola",
            lines = new[] { new { description = "Pergola design", amount = 500m } },
            split = "shares",
        });
        (await SetAsync(pending.Id, false)).TotalWithTvaUsd.ShouldBe(500m);
        (await MoneyAsync()).RevenueWithTva.ShouldBe(12_210m);
        (await Client.PostEmptyAsync($"/api/design-vos/{pending.Id}/cancel", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PutJsonAsync($"/api/design-vos/{pending.Id}/tva", new { chargeTva = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.DesignVoNotPending);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        await (await Client.PutJsonAsync($"/api/design-vos/{visits.Id}/tva", new { chargeTva = false }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Its_PDF_and_email_go_to_the_client_and_a_vo_never_takes_the_price_below_zero()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await ProjectAsync(superAdmin, [await _steps.DesignDepartmentAsync()], price: 1000m);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/design-vos",
                new { title = "Smaller scope", lines = new[] { new { description = "No interiors", amount = -1500m } }, apply = true }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.DesignVoBelowZero);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/design-vos",
                new { title = "Nothing", lines = new[] { new { description = "More", amount = 100m }, new { description = "Less", amount = -100m } } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var vo = await CreateAsync(superAdmin, project.Id, new { title = "Terrace", lines = new[] { new { description = "Terrace", amount = 1500m } } });
        var pdf = await Client.GetAsync($"/api/design-vos/{vo.Id}/pdf", superAdmin.AccessToken);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        pdf.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"{vo.Number.ToLowerInvariant()}.pdf");

        var to = AuthTestSteps.NewEmail("client");
        ((int)(await Client.PostJsonAsync($"/api/design-vos/{vo.Id}/email", new { to }, superAdmin.AccessToken)).StatusCode).ShouldBe(204);
        api.Emails.CountTo(to).ShouldBe(1);
    }

    [Fact]
    public async Task Hours_outside_the_scope_since_the_last_vo_can_be_shown_and_stay_as_they_were()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await ProjectAsync(superAdmin, [await _steps.DesignDepartmentAsync()]);
        var start = WorkEntryTestSteps.HoursAgoOnOneDay(4, 45);
        ((int)(await Client.PostJsonAsync("/api/work-entries",
                new { projectId = project.Id, description = "The client's new terrace", startedAt = start, endedAt = start.AddMinutes(45), outsideScope = true },
                designer.AccessToken)).StatusCode).ShouldBe(201);

        var since = await (await Client.GetAsync($"/api/projects/{project.Id}/design-vos/outside-scope", superAdmin.AccessToken)).ReadAsync<OutsideScopeResponse>();
        since.Minutes.ShouldBe(45);
        since.From.ShouldBe(project.StartDate);

        var vo = await CreateAsync(superAdmin, project.Id, new
        {
            title = "Terrace", lines = new[] { new { description = "Terrace", amount = 1500m } }, showOutsideScope = true, apply = true,
        });
        vo.OutsideScopeMinutes.ShouldBe(45);
        // counted from the VO just applied now
        (await (await Client.GetAsync($"/api/projects/{project.Id}/design-vos/outside-scope", superAdmin.AccessToken)).ReadAsync<OutsideScopeResponse>())
            .Minutes.ShouldBe(0);
    }

    [Fact]
    public async Task Whoever_sees_the_money_reads_them_and_whoever_writes_it_makes_them()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await ProjectAsync(superAdmin, [await _steps.DesignDepartmentAsync()]);
        var body = new { title = "Terrace", lines = new[] { new { description = "Terrace", amount = 1500m } } };

        await (await Client.GetAsync($"/api/projects/{project.Id}/design-vos", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/design-vos", body, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await (await Client.GetAsync($"/api/projects/{site.Id}/design-vos", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }
}
