using System.Net;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>A project can have its own photo, shown instead of its icon tile, seen by whoever sees its business.</summary>
public class ProjectPhotoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task A_project_takes_an_uploaded_photo_shown_in_its_page_and_the_list_and_can_go_back_to_its_icon()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        project.PhotoFileId.ShouldBeNull();

        var photo = await FileTests.UploadPngAsync(Client, superAdmin.AccessToken, "villa.png");
        var set = await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = photo.Id }, superAdmin.AccessToken));
        set.PhotoFileId.ShouldBe(photo.Id);
        (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken))).PhotoFileId.ShouldBe(photo.Id);
        var list = await OkAsync<ID_Indetails.api.DTO.Response.Common.PagedResponse<ProjectSummaryResponse>>(
            Client.GetAsync($"/api/projects?search={Uri.EscapeDataString(project.Title)}", superAdmin.AccessToken));
        list.Items.Single(p => p.Id == project.Id).PhotoFileId.ShouldBe(photo.Id);

        // a new photo replaces it; the old one is attached to nothing any more
        var next = await FileTests.UploadPngAsync(Client, superAdmin.AccessToken, "villa 2.png");
        (await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = next.Id }, superAdmin.AccessToken)))
            .PhotoFileId.ShouldBe(next.Id);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM projects WHERE photo_file_id = @id", ("@id", photo.Id))).ShouldBe(0);

        (await OkAsync<ProjectResponse>(Client.DeleteAsync($"/api/projects/{project.Id}/photo", superAdmin.AccessToken))).PhotoFileId.ShouldBeNull();
    }

    [Fact]
    public async Task Only_an_image_of_the_callers_own_not_attached_elsewhere_becomes_a_projects_photo()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var other = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // someone else's upload
        var theirs = await FileTests.UploadPngAsync(Client, admin.AccessToken);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = theirs.Id }, superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // one already another project's photo
        var photo = await FileTests.UploadPngAsync(Client, superAdmin.AccessToken);
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{other.Id}/photo", new { fileId = photo.Id }, superAdmin.AccessToken));
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = photo.Id }, superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.FileInUse);
    }

    [Fact]
    public async Task A_projects_photo_is_seen_by_whoever_sees_its_business_and_changed_only_with_project_edit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var photo = await FileTests.UploadPngAsync(Client, superAdmin.AccessToken);
        await OkAsync<ProjectResponse>(Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = photo.Id }, superAdmin.AccessToken));

        (await Client.GetAsync($"/api/files/{photo.Id}", designer.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Execution doesn't see Design's projects, so not their photos either
        (await Client.GetAsync($"/api/files/{photo.Id}", siteAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // an employee can't change it, and another business's project reads as missing
        var mine = await FileTests.UploadPngAsync(Client, designer.AccessToken);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = mine.Id }, designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/photo", designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var theirs = await FileTests.UploadPngAsync(Client, siteAdmin.AccessToken);
        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/photo", new { fileId = theirs.Id }, siteAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
