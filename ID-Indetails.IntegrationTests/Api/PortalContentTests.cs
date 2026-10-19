using System.Net.Http.Headers;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A project's files on their way to the content store: sent in parts straight to the store (the local test store
/// here, which behaves like R2's signed links), checked, then added to a section; replaced, renamed, moved and deleted;
/// the storage they use; and that nobody outside the Design project, and no client, can reach them. Who may change
/// which file, comments and the client choices are in <see cref="ProjectFilesTests"/>.
/// </summary>
public class PortalContentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const long MiB = 1024 * 1024;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[200]];
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[100]];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private sealed record IdOnly(long Id);

    private sealed record Setup(AuthResponse SuperAdmin, ProjectResponse Project, long ClientId, string ContactEmail);

    private async Task<Setup> DesignProjectAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var contact = AuthTestSteps.NewEmail("client");
        var client = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Rita Frem",
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "email", value = contact, label = "Office" } },
        }, superAdmin.AccessToken)).ReadAsync<IdOnly>();
        return new Setup(superAdmin, await ProjectAsync(superAdmin, DomainCodes.Design, client.Id), client.Id, contact);
    }

    private async Task<ProjectResponse> ProjectAsync(AuthResponse staff, string domain, long? clientId)
    {
        var title = "Project " + Guid.NewGuid().ToString("N")[..8];
        object body = clientId is null ? new { title, domainCode = domain, billingType = "internal" } : new { title, domainCode = domain, clientId };
        var response = await Client.PostJsonAsync("/api/projects", body, staff.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    private static string Base(long projectId) => $"/api/projects/{projectId}/files";

    private Task<HttpResponseMessage> StartAsync(
        AuthResponse staff, long projectId, string fileName, long size, string section = "presentation", long? replaceItemId = null, string? token = null)
        => Client.PostJsonAsync($"{Base(projectId)}/uploads",
            new { clientToken = token ?? Guid.NewGuid().ToString(), section, fileName, size, replaceItemId }, staff.AccessToken);

    private async Task<PortalUploadStartedResponse> StartedAsync(
        AuthResponse staff, long projectId, string fileName, long size, string section = "presentation", long? replaceItemId = null, string? token = null)
    {
        var response = await StartAsync(staff, projectId, fileName, size, section, replaceItemId, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalUploadStartedResponse>();
    }

    private async Task<PortalPartLinksResponse> LinksAsync(AuthResponse staff, long projectId, long uploadId, params int[] partNumbers)
    {
        var response = await Client.PostJsonAsync($"{Base(projectId)}/uploads/{uploadId}/parts", new { partNumbers }, staff.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PortalPartLinksResponse>();
    }

    /// <summary>A part goes to its link as the browser sends it: the raw bytes, with no token and no other header.</summary>
    private async Task PutPartAsync(string url, byte[] bytes)
    {
        var response = await Client.PutAsync(url, new ByteArrayContent(bytes));
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> CompleteAsync(AuthResponse staff, long projectId, long uploadId)
        => Client.PostEmptyAsync($"{Base(projectId)}/uploads/{uploadId}/complete", staff.AccessToken);

    /// <summary>Sends a whole file in its parts and completes it.</summary>
    private async Task<ProjectFileUploadCompletedResponse> UploadAsync(
        AuthResponse staff, long projectId, string fileName, byte[] bytes, string section = "presentation", long? replaceItemId = null)
    {
        var started = await StartedAsync(staff, projectId, fileName, bytes.Length, section, replaceItemId);
        var links = await LinksAsync(staff, projectId, started.UploadId, [.. Enumerable.Range(1, started.PartCount)]);
        foreach (var link in links.Parts)
        {
            var from = (int)((link.PartNumber - 1) * started.PartSize);
            await PutPartAsync(link.Url, bytes[from..(int)Math.Min(from + started.PartSize, bytes.Length)]);
        }

        var completed = await CompleteAsync(staff, projectId, started.UploadId);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        return await completed.ReadAsync<ProjectFileUploadCompletedResponse>();
    }

    private async Task<ProjectFilesResponse> ContentAsync(AuthResponse staff, long projectId)
    {
        var response = await Client.GetAsync(Base(projectId), staff.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectFilesResponse>();
    }

    private async Task RunCleanupAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPortalContentMaintenance>().CleanUpAsync();
    }

    [Fact]
    public async Task A_file_sent_in_parts_becomes_a_project_file_with_its_sizes_and_a_preview_link()
    {
        var s = await DesignProjectAsync();

        var completed = await UploadAsync(s.SuperAdmin, s.Project.Id, "Living room.jpg", Jpeg);

        completed.Item!.Title.ShouldBe("Living room");
        completed.Item.Section.ShouldBe(PortalSection.Presentation);
        // the team's until someone chooses it for the client
        completed.Item.ForClient.ShouldBeFalse();
        completed.Item.ClientState.ShouldBeNull();
        completed.Item.File.Name.ShouldBe("Living room.jpg");
        completed.Item.File.ContentType.ShouldBe("image/jpeg");
        completed.Item.File.Preview.ShouldBe(PortalPreview.Image);
        completed.Item.File.OriginalBytes.ShouldBe(Jpeg.Length);
        completed.Item.File.StoredBytes.ShouldBe(Jpeg.Length);
        completed.Item.File.UploadedBy!.Id.ShouldBe(s.SuperAdmin.User.Id);
        completed.Item.UploadedBy!.Id.ShouldBe(s.SuperAdmin.User.Id);
        completed.Usage.ShouldBe(new PortalUsageResponse(1, Jpeg.Length, Jpeg.Length));

        var content = await ContentAsync(s.SuperAdmin, s.Project.Id);
        content.StorageReady.ShouldBeTrue();
        content.CanEdit.ShouldBeTrue();
        content.AcceptedExtensions.ShouldContain("jpg");
        content.Items.Single().Id.ShouldBe(completed.Item.Id);

        // the preview link serves the bytes inline, without any sign-in
        var preview = await Client.GetAsync(content.Items.Single().PreviewUrl);
        preview.IsSuccessStatusCode.ShouldBeTrue();
        (await preview.Content.ReadAsByteArrayAsync()).ShouldBe(Jpeg);
        preview.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        preview.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");

        // a download link is an attachment under the original name
        var link = await (await Client.GetAsync($"{Base(s.Project.Id)}/items/{completed.Item.Id}/link?inline=false", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalContentLinkResponse>();
        var download = await Client.GetAsync(link.Url);
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("Living room.jpg");

        // the browser keeps each only while its link lasts: an hour for a preview, five minutes for a download
        preview.Headers.CacheControl!.Private.ShouldBeTrue();
        preview.Headers.CacheControl.MaxAge!.Value.TotalSeconds.ShouldBeInRange(3000, 3600);
        download.Headers.CacheControl!.MaxAge!.Value.TotalSeconds.ShouldBeInRange(200, 300);
    }

    [Fact]
    public async Task A_large_file_goes_in_equal_parts_and_completes_only_when_every_part_has_arrived()
    {
        var s = await DesignProjectAsync();
        var bytes = new byte[(16 * MiB) + 10];
        "%PDF-1.4"u8.CopyTo(bytes);

        var started = await StartedAsync(s.SuperAdmin, s.Project.Id, "Drawings set.pdf", bytes.Length, "drawing");
        started.PartSize.ShouldBe(16 * MiB);
        started.PartCount.ShouldBe(2);

        var links = await LinksAsync(s.SuperAdmin, s.Project.Id, started.UploadId, 1, 2);
        await PutPartAsync(links.Parts[0].Url, bytes[..(int)(16 * MiB)]);

        // the last part hasn't arrived yet
        await (await CompleteAsync(s.SuperAdmin, s.Project.Id, started.UploadId)).ShouldFailWithAsync(409, ErrorCodes.PortalUploadIncomplete);

        await PutPartAsync(links.Parts[1].Url, bytes[(int)(16 * MiB)..]);
        var completed = await (await CompleteAsync(s.SuperAdmin, s.Project.Id, started.UploadId)).ReadAsync<ProjectFileUploadCompletedResponse>();
        completed.Item!.File.OriginalBytes.ShouldBe(bytes.Length);
        completed.Item.File.Preview.ShouldBe(PortalPreview.Pdf);
        completed.Item.PreviewUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Repeating_a_start_or_a_completion_gives_the_same_upload_and_item()
    {
        var s = await DesignProjectAsync();
        var token = Guid.NewGuid().ToString();

        var first = await StartedAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length, token: token);
        var again = await StartedAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length, token: token);
        again.UploadId.ShouldBe(first.UploadId);

        var links = await LinksAsync(s.SuperAdmin, s.Project.Id, first.UploadId, 1);
        await PutPartAsync(links.Parts[0].Url, Pdf);

        // completions at once (a double click, a retried request): each waits its turn, and all get the one item
        var together = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CompleteAsync(s.SuperAdmin, s.Project.Id, first.UploadId)));
        foreach (var response in together)
        {
            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        var items = await Task.WhenAll(together.Select(r => r.ReadAsync<ProjectFileUploadCompletedResponse>()));
        items.Select(i => i.Item!.Id).Distinct().ShouldHaveSingleItem();

        var later = await (await CompleteAsync(s.SuperAdmin, s.Project.Id, first.UploadId)).ReadAsync<ProjectFileUploadCompletedResponse>();
        later.Item!.Id.ShouldBe(items[0].Item!.Id);
        (await ContentAsync(s.SuperAdmin, s.Project.Id)).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_start_repeated_before_the_store_began_the_upload_is_over_and_the_browser_starts_again()
    {
        var s = await DesignProjectAsync();
        var token = Guid.NewGuid().ToString();
        var first = await StartedAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length, token: token);

        // as if the first request were still waiting for the store, or its start had failed
        await _steps.ExecuteAsync("UPDATE portal_uploads SET store_upload_id = '' WHERE id = @id", ("@id", first.UploadId));
        await (await StartAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length, token: token)).ShouldFailWithAsync(409, ErrorCodes.PortalUploadEnded);

        var again = await StartedAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length);
        again.UploadId.ShouldNotBe(first.UploadId);

        // the same file started twice at the same moment: one upload, and never a server error
        var sameToken = Guid.NewGuid().ToString();
        var both = await Task.WhenAll(
            StartAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length, token: sameToken),
            StartAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length, token: sameToken));
        both.ShouldAllBe(r => (int)r.StatusCode == 201 || (int)r.StatusCode == 409);
        var started = await Task.WhenAll(both.Where(r => (int)r.StatusCode == 201).Select(r => r.ReadAsync<PortalUploadStartedResponse>()));
        started.ShouldNotBeEmpty();
        started.Select(u => u.UploadId).Distinct().Count().ShouldBe(1);
        (await _steps.ScalarAsync<long>(
            "SELECT COUNT(*) FROM portal_uploads WHERE client_token = @t AND created_by_id = @u", ("@t", sameToken), ("@u", s.SuperAdmin.User.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task Changes_at_the_same_moment_wait_for_each_other_instead_of_failing()
    {
        var s = await DesignProjectAsync();
        // the first file makes the content lock's row: every change below finds it there
        var kitchen = await UploadAsync(s.SuperAdmin, s.Project.Id, "Kitchen.jpg", Jpeg);
        var chosen = await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{kitchen.Item!.Id}/for-client", new { forClient = true }, s.SuperAdmin.AccessToken);
        chosen.IsSuccessStatusCode.ShouldBeTrue(await chosen.Content.ReadAsStringAsync());

        var uploads = new List<PortalUploadStartedResponse>();
        foreach (var name in new[] { "Bedroom.jpg", "Bathroom.jpg", "Hall.jpg" })
        {
            var upload = await StartedAsync(s.SuperAdmin, s.Project.Id, name, Jpeg.Length);
            await PutPartAsync((await LinksAsync(s.SuperAdmin, s.Project.Id, upload.UploadId, 1)).Parts[0].Url, Jpeg);
            uploads.Add(upload);
        }

        var completed = await Task.WhenAll(uploads.Select(u => CompleteAsync(s.SuperAdmin, s.Project.Id, u.UploadId)));
        foreach (var response in completed)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        }

        var renamed = await Task.WhenAll(Enumerable.Range(1, 4).Select(i =>
            Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{kitchen.Item.Id}", new { title = $"Kitchen {i}" }, s.SuperAdmin.AccessToken)));
        foreach (var response in renamed)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        }

        // every change happened, one after the other. Choosing the kitchen and each rename of it moved the client
        // portal's revision on; the team's own new files didn't.
        (await ContentAsync(s.SuperAdmin, s.Project.Id)).Items.Count.ShouldBe(4);
        var portal = await (await Client.GetAsync($"/api/projects/{s.Project.Id}/portal/content", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalContentResponse>();
        portal.Revision.ShouldBe(1 + 4);
    }

    [Fact]
    public async Task Files_of_the_wrong_kind_are_refused_before_and_after_sending()
    {
        var s = await DesignProjectAsync();

        await (await StartAsync(s.SuperAdmin, s.Project.Id, "page.html", 10)).ShouldFailWithAsync(400, ErrorCodes.PortalFileTypeNotAllowed);
        await (await StartAsync(s.SuperAdmin, s.Project.Id, "logo.svg", 10)).ShouldFailWithAsync(400, ErrorCodes.PortalFileTypeNotAllowed);
        await (await StartAsync(s.SuperAdmin, s.Project.Id, "empty.jpg", 0)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // named .jpg, but a web page: refused once it arrives, and removed
        var fake = "<!DOCTYPE html><html><script>alert(1)</script></html>"u8.ToArray();
        var started = await StartedAsync(s.SuperAdmin, s.Project.Id, "photo.jpg", fake.Length);
        var links = await LinksAsync(s.SuperAdmin, s.Project.Id, started.UploadId, 1);
        await PutPartAsync(links.Parts[0].Url, fake);

        await (await CompleteAsync(s.SuperAdmin, s.Project.Id, started.UploadId)).ShouldFailWithAsync(400, ErrorCodes.PortalFileContentMismatch);
        await (await CompleteAsync(s.SuperAdmin, s.Project.Id, started.UploadId)).ShouldFailWithAsync(409, ErrorCodes.PortalUploadEnded);

        var content = await ContentAsync(s.SuperAdmin, s.Project.Id);
        content.Items.ShouldBeEmpty();
        content.Usage.ShouldBe(new PortalUsageResponse(0, 0, 0));
    }

    [Fact]
    public async Task Replacing_keeps_the_item_and_its_place_and_deletes_the_old_file()
    {
        var s = await DesignProjectAsync();
        var first = await UploadAsync(s.SuperAdmin, s.Project.Id, "Kitchen.jpg", Jpeg);
        var second = await UploadAsync(s.SuperAdmin, s.Project.Id, "Bedroom.jpg", Jpeg);
        var oldPreview = (await ContentAsync(s.SuperAdmin, s.Project.Id)).Items.Single(i => i.Id == first.Item!.Id).PreviewUrl!;

        byte[] newer = [.. Jpeg, 1, 2, 3];
        var replaced = await UploadAsync(s.SuperAdmin, s.Project.Id, "Kitchen v2.jpg", newer, replaceItemId: first.Item!.Id);

        replaced.Item!.Id.ShouldBe(first.Item.Id);
        replaced.Item.Title.ShouldBe("Kitchen");
        replaced.Item.SortOrder.ShouldBe(first.Item.SortOrder);
        replaced.Item.File.Id.ShouldNotBe(first.Item.File.Id);
        replaced.Item.File.Name.ShouldBe("Kitchen v2.jpg");
        replaced.Usage.ShouldBe(new PortalUsageResponse(2, newer.Length + Jpeg.Length, newer.Length + Jpeg.Length));

        // the old file is gone from the store
        (await Client.GetAsync(oldPreview)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        (await _steps.ScalarAsync<long>("SELECT COUNT(*) FROM portal_files WHERE id = @id", ("@id", first.Item.File.Id))).ShouldBe(0);
        second.Item!.SortOrder.ShouldBeGreaterThan(first.Item.SortOrder);
    }

    [Fact]
    public async Task Items_can_be_renamed_moved_to_the_end_of_another_section_and_deleted()
    {
        var s = await DesignProjectAsync();
        var render = await UploadAsync(s.SuperAdmin, s.Project.Id, "Render.jpg", Jpeg, "model");
        var plan = await UploadAsync(s.SuperAdmin, s.Project.Id, "Plan.pdf", Pdf, "presentation");

        var renamed = await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{plan.Item!.Id}", new { title = "  Ground floor plan  " }, s.SuperAdmin.AccessToken))
            .ReadAsync<ProjectFileResponse>();
        renamed.Title.ShouldBe("Ground floor plan");

        var moved = await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{plan.Item.Id}", new { section = "model" }, s.SuperAdmin.AccessToken))
            .ReadAsync<ProjectFileResponse>();
        moved.Section.ShouldBe(PortalSection.Model);
        moved.SortOrder.ShouldBeGreaterThan(render.Item!.SortOrder);

        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{plan.Item.Id}", new { title = "" }, s.SuperAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"{Base(s.Project.Id)}/items/{plan.Item.Id}", new { title = new string('x', 201) }, s.SuperAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var deleted = await (await Client.DeleteAsync($"{Base(s.Project.Id)}/items/{plan.Item.Id}", s.SuperAdmin.AccessToken))
            .ReadAsync<PortalUsageChangedResponse>();
        deleted.Usage.Files.ShouldBe(1);
        await (await Client.GetAsync($"{Base(s.Project.Id)}/items/{plan.Item.Id}/link", s.SuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"{Base(s.Project.Id)}/items/{plan.Item.Id}", s.SuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await ContentAsync(s.SuperAdmin, s.Project.Id)).Items.Select(i => i.Id).ShouldBe([render.Item.Id]);
    }

    [Fact]
    public async Task A_cancelled_upload_can_never_complete_and_deleting_the_item_it_replaced_cancels_it()
    {
        var s = await DesignProjectAsync();
        var started = await StartedAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length);
        await PutPartAsync((await LinksAsync(s.SuperAdmin, s.Project.Id, started.UploadId, 1)).Parts[0].Url, Pdf);

        (await Client.DeleteAsync($"{Base(s.Project.Id)}/uploads/{started.UploadId}", s.SuperAdmin.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"{Base(s.Project.Id)}/uploads/{started.UploadId}", s.SuperAdmin.AccessToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await CompleteAsync(s.SuperAdmin, s.Project.Id, started.UploadId)).ShouldFailWithAsync(409, ErrorCodes.PortalUploadEnded);
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, s.SuperAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.PortalUploadEnded);

        // a replacement on its way when its item is deleted has nothing left to replace
        var item = await UploadAsync(s.SuperAdmin, s.Project.Id, "Kitchen.jpg", Jpeg);
        var replacement = await StartedAsync(s.SuperAdmin, s.Project.Id, "Kitchen v2.jpg", Jpeg.Length, replaceItemId: item.Item!.Id);
        await PutPartAsync((await LinksAsync(s.SuperAdmin, s.Project.Id, replacement.UploadId, 1)).Parts[0].Url, Jpeg);
        (await Client.DeleteAsync($"{Base(s.Project.Id)}/items/{item.Item.Id}", s.SuperAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await CompleteAsync(s.SuperAdmin, s.Project.Id, replacement.UploadId)).ShouldFailWithAsync(409, ErrorCodes.PortalUploadEnded);
        (await ContentAsync(s.SuperAdmin, s.Project.Id)).Usage.ShouldBe(new PortalUsageResponse(0, 0, 0));
    }

    [Fact]
    public async Task Abandoned_uploads_are_cleaned_up_with_what_reached_the_store()
    {
        var s = await DesignProjectAsync();
        var started = await StartedAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf.Length);
        await PutPartAsync((await LinksAsync(s.SuperAdmin, s.Project.Id, started.UploadId, 1)).Parts[0].Url, Pdf);

        // nothing for more than a day
        await _steps.ExecuteAsync(
            "UPDATE portal_uploads SET last_activity_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 25 HOUR) WHERE id = @id", ("@id", started.UploadId));
        await RunCleanupAsync();

        (await _steps.ScalarAsync<string>("SELECT status FROM portal_uploads WHERE id = @id", ("@id", started.UploadId))).ShouldBe("expired");
        await (await CompleteAsync(s.SuperAdmin, s.Project.Id, started.UploadId)).ShouldFailWithAsync(409, ErrorCodes.PortalUploadEnded);
    }

    [Fact]
    public async Task Nobody_outside_the_Design_project_reaches_its_files_or_someone_elses_upload()
    {
        var s = await DesignProjectAsync();
        var item = await UploadAsync(s.SuperAdmin, s.Project.Id, "plan.pdf", Pdf);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(s.SuperAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(s.SuperAdmin, RoleCodes.Admin, DomainCodes.Execution);

        (await ContentAsync(designAdmin, s.Project.Id)).Items.Single().Id.ShouldBe(item.Item!.Id);
        // Design's files read as not found for someone who can't see Design
        await (await Client.GetAsync(Base(s.Project.Id), executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"{Base(s.Project.Id)}/items/{item.Item.Id}/link", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // an Execution site has no files here, even for a super admin
        var siteClient = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Site client",
            label = "Client",
            domainCode = DomainCodes.Execution,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("site") } },
        }, s.SuperAdmin.AccessToken)).ReadAsync<IdOnly>();
        var site = await ProjectAsync(s.SuperAdmin, DomainCodes.Execution, siteClient.Id);
        await (await Client.GetAsync(Base(site.Id), s.SuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);

        // another project's item, or someone else's upload, reads as not found
        var other = await ProjectAsync(s.SuperAdmin, DomainCodes.Design, s.ClientId);
        await (await Client.GetAsync($"{Base(other.Id)}/items/{item.Item.Id}/link", s.SuperAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var started = await StartedAsync(s.SuperAdmin, s.Project.Id, "mine.pdf", Pdf.Length);
        await (await CompleteAsync(designAdmin, s.Project.Id, started.UploadId)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"{Base(s.Project.Id)}/uploads/{started.UploadId}/parts", new { partNumbers = new[] { 1 } }, designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_client_with_access_to_the_project_cannot_reach_its_files()
    {
        var s = await DesignProjectAsync();
        var item = await UploadAsync(s.SuperAdmin, s.Project.Id, "Living room.jpg", Jpeg);
        var invited = await Client.PostJsonAsync($"/api/projects/{s.Project.Id}/portal/invitations", new { email = s.ContactEmail }, s.SuperAdmin.AccessToken);
        invited.IsSuccessStatusCode.ShouldBeTrue(await invited.Content.ReadAsStringAsync());
        var accepted = await Client.PostJsonAsync("/api/portal/invitations/accept", new
        {
            token = api.Emails.InvitationTokenFor(s.ContactEmail), firstName = "Rita", lastName = "Frem", password = "client-own-passphrase",
        });
        var client = await accepted.ReadAsync<PortalAuthResponse>();

        // the staff endpoints refuse a client's token outright
        foreach (var url in new[] { Base(s.Project.Id), $"{Base(s.Project.Id)}/items/{item.Item!.Id}/link", $"{Base(s.Project.Id)}/items/{item.Item.Id}/comments" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.AccessToken);
            (await Client.SendAsync(request)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        }

        // and a made-up or altered link reads nothing
        var preview = (await ContentAsync(s.SuperAdmin, s.Project.Id)).Items.Single().PreviewUrl!;
        (await Client.GetAsync(preview[..^4] + "AAAA")).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        (await Client.GetAsync("/api/content-store/objects/not-a-token")).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
