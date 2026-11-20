using System.Net;
using System.Text.Json.Nodes;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A Design project's departments with a scripted model: Proton reads them as the endpoint shows them to the caller,
/// proposes choosing them (standard card) and their shares (strong card, the percentages the user's own words, the
/// project's name typed), and the log-work card says when the logger's department isn't on the project. Every test
/// follows its own project, since the classes share one database.
/// </summary>
public class ProtonDepartmentActionsTests(ProtonAllActionsApiFactory api) : IClassFixture<ProtonAllActionsApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

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

    private static string Message(JsonNode result) => result["message"]!.GetValue<string>();

    /// <summary>A Design client project of 10,000 before TVA, with <paramref name="departmentIds"/>.</summary>
    private async Task<ProjectResponse> ProjectAsync(AuthResponse superAdmin, IReadOnlyList<long> departmentIds)
    {
        var client = await Projects.CreateClientAsync(superAdmin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new
        {
            title = "Villa " + Guid.NewGuid().ToString("N")[..8],
            domainCode = DomainCodes.Design,
            clientId = client.Id,
            revenue = 10000m,
            tvaRate = 0m,
            departmentIds,
        }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        return await created.ReadAsync<ProjectResponse>();
    }

    private async Task<ProjectDepartmentsResponse> DepartmentsAsync(AuthResponse who, long projectId)
        => await (await Client.GetAsync($"/api/projects/{projectId}/departments", who.AccessToken)).ReadAsync<ProjectDepartmentsResponse>();

    private async Task<long> NewDepartmentAsync(AuthResponse superAdmin, string name)
        => (await _steps.CreateTeamAsync(superAdmin, DomainCodes.Design, name + " " + Guid.NewGuid().ToString("N")[..6])).Id;

    [Fact]
    public async Task Departments_are_read_with_money_only_for_whoever_sees_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await NewDepartmentAsync(superAdmin, "Structure");
        var project = await ProjectAsync(superAdmin, [architecture, structure]);
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/departments/plan",
                new { departments = new[] { new { teamId = architecture, plannedHours = 10m } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var (_, withMoney) = await ProposeAsync(superAdmin, "get_project_departments", new { project_id = project.Id });
        var data = withMoney["data"]!;
        var rows = data["departments"]!.AsArray();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r!["share_percent"]!.GetValue<decimal>() == 50m && r["budget"]!.GetValue<decimal>() == 5000m && r["left"]!.GetValue<decimal>() == 5000m);
        // the planned hours, and progress by hours: nothing logged yet of 600 planned minutes
        var planned = rows.Single(r => r!["id"]!.GetValue<long>() == architecture)!;
        planned["planned_minutes"]!.GetValue<int>().ShouldBe(600);
        planned["progress_pct"]!.GetValue<int>().ShouldBe(0);
        rows.Single(r => r!["id"]!.GetValue<long>() == structure)!["planned_minutes"].ShouldBeNull();
        data["history"]!.AsArray().ShouldHaveSingleItem()!["kind"]!.GetValue<string>().ShouldBe("Created");

        // an admin without financial:view sees who works on it and nothing about money or the history
        var (_, plain) = await ProposeAsync(admin, "get_project_departments", new { project_id = project.Id });
        var plainRows = plain["data"]!["departments"]!.AsArray();
        plainRows.Count.ShouldBe(2);
        plainRows.ShouldAllBe(r => r!["share_percent"] == null && r["budget"] == null && r["people"] == null);
        plainRows.Single(r => r!["id"]!.GetValue<long>() == architecture)!["planned_minutes"]!.GetValue<int>().ShouldBe(600);
        plain["data"]!["history"].ShouldBeNull();

        // the project's own card names its departments
        var (_, card) = await ProposeAsync(superAdmin, "get_project", new { project_id = project.Id });
        card["data"]!["departments"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_departments_are_chosen_on_a_card_and_an_employee_is_offered_nothing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await NewDepartmentAsync(superAdmin, "Structure");
        var project = await ProjectAsync(superAdmin, [architecture]);

        var (card, _) = await ProposeAsync(superAdmin, "propose_project_departments", new { project_id = project.Id, department_ids = new[] { architecture, structure } });
        var preview = card.PendingAction.ShouldNotBeNull().Preview;
        preview.Confirm.ShouldBe(ProtonConfirm.Standard);
        preview.Changes.Select(c => c.Label).ShouldBe(["Added", "Shares"]);
        preview.Note.ShouldNotBeNull().ShouldContain("go through");
        (await DepartmentsAsync(superAdmin, project.Id)).Departments.Count.ShouldBe(1);

        await ConfirmAsync(superAdmin, card);
        var after = await DepartmentsAsync(superAdmin, project.Id);
        after.Departments.Select(d => d.Id).ShouldBe([architecture, structure], ignoreOrder: true);
        after.Departments.ShouldAllBe(d => d.Share == 0.5m);

        // the same departments again, none at all, and an Execution site are said before any card
        var (_, same) = await ProposeAsync(superAdmin, "propose_project_departments", new { project_id = project.Id, department_ids = new[] { architecture, structure } });
        Message(same).ShouldContain("already the project's departments");
        var (_, none) = await ProposeAsync(superAdmin, "propose_project_departments", new { project_id = project.Id, department_ids = Array.Empty<long>() });
        Message(none).ShouldContain("Which departments");
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var (_, execution) = await ProposeAsync(superAdmin, "propose_project_departments", new { project_id = site.Id, department_ids = new[] { architecture } });
        Message(execution).ShouldContain("Only a Design project");

        // taking one off shows as removed, and a department with hours stays
        var (removal, _) = await ProposeAsync(superAdmin, "propose_project_departments", new { project_id = project.Id, department_ids = new[] { architecture } });
        removal.PendingAction!.Preview.Changes.Select(c => c.Label).ShouldBe(["Taken off", "Shares"]);

        // without project:edit the tool is not offered
        var (_, unavailable) = await ProposeAsync(designer, "propose_project_departments", new { project_id = project.Id, department_ids = new[] { architecture } });
        Message(unavailable).ShouldContain("isn't available to this user");
    }

    [Fact]
    public async Task Shares_are_set_on_a_strong_card_with_the_percentages_the_user_typed_and_the_project_name()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await NewDepartmentAsync(superAdmin, "Structure");
        var project = await ProjectAsync(superAdmin, [architecture, structure]);
        var shares = new[] { new { department_id = architecture, percent = 70m }, new { department_id = structure, percent = 30m } };

        // a percentage the user never typed is sent back
        var (_, untyped) = await ProposeAsync(superAdmin, "propose_department_shares", new { project_id = project.Id, shares });
        Message(untyped).ShouldContain("didn't give");

        var (card, _) = await ProposeAsync(superAdmin, "propose_department_shares",
            new { project_id = project.Id, shares, reason = "Structure is lighter" }, "Make it 70 and 30, Structure is lighter");
        var preview = card.PendingAction.ShouldNotBeNull().Preview;
        (preview.Confirm, preview.Phrase).ShouldBe((ProtonConfirm.Typed, project.Title));
        preview.Changes.Select(c => c.After).ShouldBe(["70%", "30%", "Structure is lighter"], ignoreOrder: true);
        (await DepartmentsAsync(superAdmin, project.Id)).Departments.ShouldAllBe(d => d.Share == 0.5m);

        await ConfirmAsync(superAdmin, card, project.Title);
        var after = await DepartmentsAsync(superAdmin, project.Id);
        after.Departments.Single(d => d.Id == architecture).Money!.Budget.ShouldBe(7000m);
        after.Departments.Single(d => d.Id == structure).Money!.Budget.ShouldBe(3000m);

        // shares that don't add up to 100 are refused before any card
        var (_, short100) = await ProposeAsync(superAdmin, "propose_department_shares",
            new { project_id = project.Id, shares = new[] { new { department_id = architecture, percent = 60m }, new { department_id = structure, percent = 30m } } }, "60 and 30");
        Message(short100).ShouldContain("100%");

        // financial:write is what offers it
        var (_, unavailable) = await ProposeAsync(admin, "propose_department_shares", new { project_id = project.Id, shares }, "70 and 30");
        Message(unavailable).ShouldContain("isn't available to this user");
    }

    [Fact]
    public async Task The_log_card_says_hours_wait_for_a_missing_department_and_refuses_someone_with_none()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, engineer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, member) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, loner) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var architecture = await _steps.DesignDepartmentAsync();
        var structure = await NewDepartmentAsync(superAdmin, "Structure");
        await _steps.PutInTeamAsync(engineer.User.Id, structure);
        await _steps.PutInTeamAsync(member.User.Id, architecture);
        await _steps.ExecuteAsync("UPDATE users SET team_id = NULL WHERE id = @u", ("@u", loner.User.Id));
        var project = await ProjectAsync(superAdmin, [architecture]);
        var yesterday = WorkEntryTestSteps.CompanyDay(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        object Log() => new { project_id = project.Id, day = yesterday, start = "10:00", end = "11:00", description = "Layouts" };

        // another department: the card stays, with the wait in its note
        var (waiting, _) = await ProposeAsync(engineer, "propose_log_work", Log());
        waiting.PendingAction.ShouldNotBeNull().Preview.Note.ShouldNotBeNull().ShouldContain("wait");

        // one of the project's own: no wait
        var (own, _) = await ProposeAsync(member, "propose_log_work", Log());
        (own.PendingAction.ShouldNotBeNull().Preview.Note ?? "").ShouldNotContain("wait");

        // no department at all: refused with the API's own words
        var (none, refused) = await ProposeAsync(loner, "propose_log_work", Log());
        none.PendingAction.ShouldBeNull();
        Message(refused).ShouldContain("aren't in a department");
    }
}
