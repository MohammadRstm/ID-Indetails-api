using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Reports;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The prototype's wider searches and its "internal (no client)" filters.</summary>
public class ListFilterTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Searches_reach_project_descriptions_client_notes_and_contacts_and_archived_clients_can_be_listed_too()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await _steps.ExecuteAsync("UPDATE projects SET description = 'Rooftop terrace in Gemmayze' WHERE id = @id", ("@id", project.Id));
        await _steps.ExecuteAsync("UPDATE clients SET note = 'Prefers calls after noon' WHERE id = @id", ("@id", project.Client!.Id));
        var contact = await _steps.ScalarAsync<string>("SELECT value FROM client_contacts WHERE client_id = @id", ("@id", project.Client!.Id));

        (await OkAsync<PagedResponse<ProjectSummaryResponse>>("/api/projects?search=gemmayze", admin.AccessToken))
            .Items.ShouldHaveSingleItem().Id.ShouldBe(project.Id);
        (await OkAsync<PagedResponse<ClientSummaryResponse>>("/api/clients?search=noon", admin.AccessToken))
            .Items.ShouldHaveSingleItem().Id.ShouldBe(project.Client!.Id);
        (await OkAsync<PagedResponse<ClientSummaryResponse>>($"/api/clients?search={Uri.EscapeDataString(contact)}", admin.AccessToken))
            .Items.ShouldHaveSingleItem().Id.ShouldBe(project.Client!.Id);

        var archived = await Projects.CreateClientAsync(admin.AccessToken, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/clients/{archived.Id}/archive", admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await OkAsync<PagedResponse<ClientSummaryResponse>>("/api/clients", admin.AccessToken)).Items.ShouldNotContain(c => c.Id == archived.Id);
        var all = (await OkAsync<PagedResponse<ClientSummaryResponse>>("/api/clients?includeArchived=true&pageSize=100", admin.AccessToken))
            .Items.Select(c => c.Id).ToList();
        all.ShouldContain(project.Client!.Id);
        all.ShouldContain(archived.Id);
        await (await Client.GetAsync("/api/clients?archived=true&includeArchived=true", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Activity_and_the_work_report_can_keep_to_internal_work_or_one_client()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await new WorkEntryTestSteps(_steps).SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var clientWork = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var created = await Client.PostJsonAsync("/api/projects", new { title = "Studio tooling", billingType = "internal" }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var tooling = await created.ReadAsync<ProjectResponse>();

        var work = new WorkEntryTestSteps(_steps);
        await work.InsertAsync(designer.User.Id, clientWork.Id, "2026-06-10 06:00:00", "2026-06-10 08:00:00");
        await work.InsertAsync(designer.User.Id, tooling.Id, "2026-06-11 06:00:00", "2026-06-11 07:00:00");

        var internalEvents = await OkAsync<PagedResponse<ActivityItemResponse>>("/api/activity?internal=true&pageSize=100", admin.AccessToken);
        internalEvents.Items.ShouldNotBeEmpty();
        internalEvents.Items.ShouldAllBe(i => i.Project != null && i.Project.Id == tooling.Id);
        await (await Client.GetAsync($"/api/activity?internal=true&clientId={clientWork.Client!.Id}", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        const string june = "from=2026-06-01&to=2026-06-30";
        (await OkAsync<WorkReportResponse>($"/api/reports/work?{june}", admin.AccessToken)).TotalMinutes.ShouldBe(180);
        (await OkAsync<WorkReportResponse>($"/api/reports/work?{june}&internal=true", admin.AccessToken)).TotalMinutes.ShouldBe(60);
        (await OkAsync<WorkReportResponse>($"/api/reports/work?{june}&clientId={clientWork.Client!.Id}", admin.AccessToken)).TotalMinutes.ShouldBe(120);
    }
}
