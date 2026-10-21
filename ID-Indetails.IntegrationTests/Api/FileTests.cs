using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SkiaSharp;

namespace ID_Indetails.IntegrationTests.Api;

public class FileTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>The PNG signature and some bytes: enough to be recognised, which is all the API checks.</summary>
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, 64).Select(i => (byte)i)];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string name, string? accessToken)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files") { Content = form };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request);
    }

    public static async Task<FileResponse> UploadPngAsync(HttpClient client, string accessToken, string name = "plan.png")
    {
        var response = await UploadAsync(client, Png, name, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<FileResponse>();
    }

    [Fact]
    public async Task People_upload_photos_and_only_they_can_read_one_not_yet_attached()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var file = await UploadPngAsync(Client, employee.AccessToken, "../../plans/stair plan.PNG");
        file.Name.ShouldBe("stair plan.png");
        file.MimeType.ShouldBe("image/png");
        file.Kind.ShouldBe(FileKind.Image);
        file.SizeBytes.ShouldBe(Png.Length);

        var download = await Client.GetAsync($"/api/files/{file.Id}", employee.AccessToken);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        download.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(Png);

        await (await Client.GetAsync($"/api/files/{file.Id}", colleague.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await UploadAsync(Client, Png, "plan.png", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_photo_is_stored_made_small_and_a_smaller_copy_comes_on_request()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        byte[] photo;
        using (var bitmap = new SKBitmap(3200, 2000))
        {
            using (var canvas = new SKCanvas(bitmap))
            {
                using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 2000), [new SKColor(90, 140, 210), new SKColor(240, 200, 150)], SKShaderTileMode.Clamp) };
                canvas.DrawRect(0, 0, 3200, 2000, sky);
                using var hill = new SKPaint { Color = new SKColor(60, 110, 70), IsAntialias = true };
                canvas.DrawOval(1600, 2000, 2240, 900, hill);
            }

            using var png = bitmap.PeekPixels().Encode(SKEncodedImageFormat.Png, 100);
            photo = png.ToArray();
        }

        var response = await UploadAsync(Client, photo, "site visit.png", superAdmin.AccessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var file = await response.ReadAsync<FileResponse>();
        file.Name.ShouldBe("site visit.webp");
        file.MimeType.ShouldBe("image/webp");
        file.SizeBytes.ShouldBeLessThan(photo.Length / 4);

        var stored = await (await Client.GetAsync($"/api/files/{file.Id}", superAdmin.AccessToken)).Content.ReadAsByteArrayAsync();
        stored.Length.ShouldBe((int)file.SizeBytes);
        using (var codec = SKCodec.Create(new MemoryStream(stored)))
        {
            codec.Info.Size.ShouldBe(new SKSizeI(1920, 1200));
        }

        // a preview asks for a small copy, made once and kept
        var preview = await Client.GetAsync($"/api/files/{file.Id}?size=160", superAdmin.AccessToken);
        preview.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        var small = await preview.Content.ReadAsByteArrayAsync();
        small.Length.ShouldBeLessThan(stored.Length / 10);
        using (var codec = SKCodec.Create(new MemoryStream(small)))
        {
            codec.Info.Size.ShouldBe(new SKSizeI(160, 100));
        }

        (await (await Client.GetAsync($"/api/files/{file.Id}?size=160", superAdmin.AccessToken)).Content.ReadAsByteArrayAsync()).ShouldBe(small);

        // a copy of 480 px and up carries the invisible mark, and the check finds which picture it is
        var marked = await (await Client.GetAsync($"/api/files/{file.Id}?size=960", superAdmin.AccessToken)).Content.ReadAsByteArrayAsync();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(marked), "file", "found online.webp" } };
        using var check = new HttpRequestMessage(HttpMethod.Post, "/api/website/watermark-check") { Content = form };
        check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var reading = await (await Client.SendAsync(check)).ReadAsync<WatermarkCheckResponse>();
        reading.Found.ShouldBeTrue();
        reading.FileId.ShouldBe(file.Id);
    }

    [Fact]
    public async Task Only_real_photos_and_videos_are_accepted_whatever_they_are_called()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        await (await UploadAsync(Client, Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"), "logo.png", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);
        await (await UploadAsync(Client, [], "empty.png", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);
    }

    [Fact]
    public async Task Uploads_attached_to_nothing_for_a_day_are_deleted_with_their_content()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var old = await UploadPngAsync(Client, superAdmin.AccessToken);
        var fresh = await UploadPngAsync(Client, superAdmin.AccessToken);
        var key = await _steps.ScalarAsync<string>("SELECT storage_key FROM files WHERE id = @id", ("@id", old.Id));
        await _steps.ExecuteAsync("UPDATE files SET created_at = DATE_SUB(UTC_TIMESTAMP(), INTERVAL 2 DAY) WHERE id = @id", ("@id", old.Id));

        await using (var scope = api.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IFileService>().DeleteUnusedAsync()).ShouldBeGreaterThanOrEqualTo(1);
        }

        await (await Client.GetAsync($"/api/files/{old.Id}", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        File.Exists(Path.Combine(api.FilesRoot, key)).ShouldBeFalse();
        (await Client.GetAsync($"/api/files/{fresh.Id}", superAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_profile_photo_is_an_upload_of_the_callers_and_everyone_signed_in_sees_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var photo = await UploadPngAsync(Client, employee.AccessToken, "me.png");
        var me = await OkAsync<CurrentUserResponse>(Client.PutJsonAsync("/api/me/photo", new { fileId = photo.Id }, employee.AccessToken));
        me.PhotoFileId.ShouldBe(photo.Id);
        (await Client.GetAsync($"/api/files/{photo.Id}", colleague.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetAsync($"/api/files/{photo.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Someone else's upload, one already attached, or a video can't be a photo.
        var theirs = await UploadPngAsync(Client, colleague.AccessToken);
        await (await Client.PutJsonAsync("/api/me/photo", new { fileId = theirs.Id }, employee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PutJsonAsync("/api/me/photo", new { fileId = photo.Id }, colleague.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        var video = await UploadAsync(Client, Mp4, "clip.mp4", employee.AccessToken);
        ((int)video.StatusCode).ShouldBe(201, await video.Content.ReadAsStringAsync());
        await (await Client.PutJsonAsync("/api/me/photo", new { fileId = (await video.ReadAsync<FileResponse>()).Id }, employee.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);

        (await OkAsync<CurrentUserResponse>(Client.DeleteAsync("/api/me/photo", employee.AccessToken))).PhotoFileId.ShouldBeNull();

        // Admins set the photo of the people they manage, from their own upload.
        var portrait = await UploadPngAsync(Client, designAdmin.AccessToken, "portrait.png");
        var profile = await OkAsync<EmployeeResponse>(
            Client.PutJsonAsync($"/api/employees/{employee.User.Id}/photo", new { fileId = portrait.Id }, designAdmin.AccessToken));
        profile.PhotoFileId.ShouldBe(portrait.Id);
        // Lists of people carry it too.
        var schedules = await OkAsync<SchedulesResponse>(Client.GetAsync("/api/company/schedules?domain=design", superAdmin.AccessToken));
        schedules.People.Single(p => p.UserId == employee.User.Id).PhotoFileId.ShouldBe(portrait.Id);
        var contacts = await OkAsync<List<ContactResponse>>(Client.GetAsync("/api/messages/contacts", colleague.AccessToken));
        contacts.Single(c => c.Id == employee.User.Id).PhotoFileId.ShouldBe(portrait.Id);

        var another = await UploadPngAsync(Client, colleague.AccessToken);
        await (await Client.PutJsonAsync($"/api/employees/{employee.User.Id}/photo", new { fileId = another.Id }, colleague.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        var sitePhoto = await UploadPngAsync(Client, siteAdmin.AccessToken);
        await (await Client.PutJsonAsync($"/api/employees/{employee.User.Id}/photo", new { fileId = sitePhoto.Id }, siteAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await OkAsync<EmployeeResponse>(Client.DeleteAsync($"/api/employees/{employee.User.Id}/photo", designAdmin.AccessToken)))
            .PhotoFileId.ShouldBeNull();
    }

    [Fact]
    public async Task The_company_logo_is_shown_to_everyone_on_the_sign_in_page()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        (await OkAsync<BrandingResponse>(Client.GetAsync("/api/company/branding"))).ShouldBe(new BrandingResponse(null, false, null));
        // the logo needn't wait for the rest of the settings: they still read as not set up
        var logoFile = await UploadPngAsync(Client, superAdmin.AccessToken, "logo.png");
        var first = await OkAsync<CompanySettingsResponse>(Client.PutJsonAsync("/api/company/logo", new { fileId = logoFile.Id }, superAdmin.AccessToken));
        first.LogoFileId.ShouldBe(logoFile.Id);
        first.IsSetUp.ShouldBeFalse();
        (await OkAsync<BrandingResponse>(Client.GetAsync("/api/company/branding"))).ShouldBe(new BrandingResponse(null, true, logoFile.Id));

        // saving the settings afterwards keeps it
        var saved = await OkAsync<CompanySettingsResponse>(
            Client.PutJsonAsync("/api/company/settings", new { name = "Indetails", defaultCurrency = "USD" }, superAdmin.AccessToken));
        saved.IsSetUp.ShouldBeTrue();
        saved.LogoFileId.ShouldBe(logoFile.Id);

        (await OkAsync<BrandingResponse>(Client.GetAsync("/api/company/branding"))).ShouldBe(new BrandingResponse("Indetails", true, logoFile.Id));
        var logo = await Client.GetAsync("/api/company/logo");
        logo.StatusCode.ShouldBe(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await logo.Content.ReadAsByteArrayAsync()).ShouldBe(Png);

        var theirs = await UploadPngAsync(Client, admin.AccessToken);
        await (await Client.PutJsonAsync("/api/company/logo", new { fileId = theirs.Id }, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        (await OkAsync<CompanySettingsResponse>(Client.DeleteAsync("/api/company/logo", superAdmin.AccessToken))).LogoFileId.ShouldBeNull();
        (await OkAsync<BrandingResponse>(Client.GetAsync("/api/company/branding"))).ShouldBe(new BrandingResponse("Indetails", false, null));
        await (await Client.GetAsync("/api/company/logo")).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Attachments_can_be_read_by_whoever_may_read_the_message_or_the_work_entry()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await new WorkEntryTestSteps(_steps).SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, carl) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        // A direct message: both people read its photo, nobody else does.
        var shared = await UploadPngAsync(Client, anna.AccessToken, "sketch.png");
        (await Client.PostJsonAsync($"/api/messages/direct/{ben.User.Id}", new { body = "", attachmentIds = new[] { shared.Id } }, anna.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.GetAsync($"/api/files/{shared.Id}", ben.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await Client.GetAsync($"/api/files/{shared.Id}", carl.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/messages/direct/{carl.User.Id}", new { body = "again", attachmentIds = new[] { shared.Id } }, anna.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileInUse);

        // A work entry: its author and whoever sees everyone's work read it.
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var proof = await UploadPngAsync(Client, anna.AccessToken, "proof.png");
        var start = WorkEntryTestSteps.HoursAgo(3);
        var logged = await Client.PostJsonAsync("/api/work-entries",
            new { projectId = project.Id, description = "Facade study", startedAt = start, endedAt = start.AddHours(1), attachmentFileId = proof.Id },
            anna.AccessToken);
        ((int)logged.StatusCode).ShouldBe(201, await logged.Content.ReadAsStringAsync());
        (await logged.ReadAsync<WorkEntryResponse>()).Attachment!.Id.ShouldBe(proof.Id);
        (await Client.GetAsync($"/api/files/{proof.Id}", designAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await Client.GetAsync($"/api/files/{proof.Id}", carl.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    /// <summary>An MP4's opening box: its size, "ftyp" and a known brand.</summary>
    private static readonly byte[] Mp4 = [0x00, 0x00, 0x00, 0x18, .. Encoding.ASCII.GetBytes("ftypisom"), .. new byte[32]];

    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }
}
