using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Execution;
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

    /// <summary>A real photo the server can read and make copies of: a small sky over a hill, as a PNG.</summary>
    public static readonly byte[] Photo = PhotoOf(64, 48);

    /// <summary>An iPhone photo's opening box (HEIC): recognised as a photo, but the server can't read it.</summary>
    public static readonly byte[] Heic = [0x00, 0x00, 0x00, 0x18, .. Encoding.ASCII.GetBytes("ftypheic"), .. new byte[32]];

    public static byte[] PhotoOf(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), [new SKColor(90, 140, 210), new SKColor(240, 200, 150)], SKShaderTileMode.Clamp) };
            canvas.DrawRect(0, 0, width, height, sky);
            using var hill = new SKPaint { Color = new SKColor(60, 110, 70), IsAntialias = true };
            canvas.DrawOval(width / 2f, height, width * 0.7f, height * 0.45f, hill);
        }

        using var png = bitmap.PeekPixels().Encode(SKEncodedImageFormat.Png, 100)!;
        return png.ToArray();
    }

    /// <summary>
    /// A PNG of a few dozen bytes whose header says it is <paramref name="width"/> × <paramref name="height"/>: reading it
    /// whole would take width × height × 4 bytes of memory.
    /// </summary>
    public static byte[] DeclaredPng(int width, int height)
    {
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 1; // one bit a pixel, grey: what makes such a file tiny
        byte[] emptyDeflate = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        return [.. Png[..8], .. Chunk("IHDR", header), .. Chunk("IDAT", emptyDeflate), .. Chunk("IEND", [])];

        static byte[] Chunk(string type, byte[] data)
        {
            byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
            var length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            var crc = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
            return [.. length, .. typed, .. crc];
        }

        static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return ~crc;
        }
    }

    /// <summary>The first bytes of a WebM (EBML header), an Ogg and an M4A: recordings, as far as the API can tell.</summary>
    public static readonly byte[] WebmVoice = [0x1A, 0x45, 0xDF, 0xA3, .. Enumerable.Range(0, 64).Select(i => (byte)i)];

    public static readonly byte[] OggVoice = [.. Encoding.ASCII.GetBytes("OggS"), .. Enumerable.Range(0, 64).Select(i => (byte)i)];

    public static readonly byte[] M4aVoice = [0x00, 0x00, 0x00, 0x18, .. Encoding.ASCII.GetBytes("ftypM4A "), .. new byte[32]];

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string name, string? accessToken, string route = "/api/files")
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", name);
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = form };
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

    /// <summary>Uploads a voice note (a WebM unless told otherwise) and returns its file.</summary>
    public static async Task<FileResponse> UploadVoiceNoteAsync(HttpClient client, string accessToken, byte[]? bytes = null, string name = "note.webm")
    {
        var response = await UploadAsync(client, bytes ?? WebmVoice, name, accessToken, "/api/files/voice");
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
    public async Task A_photo_declaring_more_pixels_than_the_server_may_read_is_refused_without_reading_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // 10,000 × 6,000 is 60 MP: about 240 MB to read whole, in a file of a few dozen bytes
        var bomb = DeclaredPng(10_000, 6_000);
        bomb.Length.ShouldBeLessThan(200);
        ID_Indetails.api.Services.Files.Implementation.Pictures.PixelsOf(bomb).ShouldBe(60_000_000);
        ID_Indetails.api.Services.Files.Implementation.Pictures.Shrink(bomb, 160).ShouldBeNull();

        await (await UploadAsync(Client, bomb, "huge.png", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.FileTooLarge);
        // within the limit, the same kind of file is read
        ID_Indetails.api.Services.Files.Implementation.Pictures.IsTooLarge(DeclaredPng(8_000, 6_000)).ShouldBeFalse();
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

            using var png = bitmap.PeekPixels().Encode(SKEncodedImageFormat.Png, 100)!;
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
            codec!.Info.Size.ShouldBe(new SKSizeI(1920, 1200));
        }

        // a preview asks for a small copy, made once and kept
        var preview = await Client.GetAsync($"/api/files/{file.Id}?size=160", superAdmin.AccessToken);
        preview.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        var small = await preview.Content.ReadAsByteArrayAsync();
        small.Length.ShouldBeLessThan(stored.Length / 10);
        using (var codec = SKCodec.Create(new MemoryStream(small)))
        {
            codec!.Info.Size.ShouldBe(new SKSizeI(160, 100));
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
    public async Task A_voice_note_is_stored_as_the_recording_its_bytes_say_it_is()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        // the name and the browser's type are never trusted: a WebM called a PNG is still a WebM recording
        var webm = await UploadVoiceNoteAsync(Client, superAdmin.AccessToken, name: "../../note.png");
        webm.Kind.ShouldBe(FileKind.Audio);
        webm.MimeType.ShouldBe("audio/webm");
        webm.Name.ShouldBe("note.webm");
        webm.SizeBytes.ShouldBe(WebmVoice.Length);

        var ogg = await UploadVoiceNoteAsync(Client, superAdmin.AccessToken, OggVoice, "memo.mp3");
        (ogg.Kind, ogg.MimeType, ogg.Name).ShouldBe((FileKind.Audio, "audio/ogg", "memo.ogg"));
        var m4a = await UploadVoiceNoteAsync(Client, superAdmin.AccessToken, M4aVoice, "memo.wav");
        (m4a.Kind, m4a.MimeType, m4a.Name).ShouldBe((FileKind.Audio, "audio/mp4", "memo.m4a"));

        var played = await Client.GetAsync($"/api/files/{webm.Id}", superAdmin.AccessToken);
        played.StatusCode.ShouldBe(HttpStatusCode.OK);
        played.Content.Headers.ContentType!.MediaType.ShouldBe("audio/webm");
        played.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await played.Content.ReadAsByteArrayAsync()).ShouldBe(WebmVoice);
    }

    [Fact]
    public async Task Only_recordings_are_accepted_as_voice_notes_and_audio_stays_out_of_the_photo_and_video_route()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();

        foreach (var notARecording in new[] { Png, Heic, Encoding.ASCII.GetBytes("%PDF-1.7 hello world"), Array.Empty<byte>() })
        {
            var refused = await UploadAsync(Client, notARecording, "note.webm", superAdmin.AccessToken, "/api/files/voice");
            await refused.ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);
        }

        (await UploadAsync(Client, Png, "note.webm", null, "/api/files/voice")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // POST /api/files has no audio: Ogg is refused and a WebM is still a video there
        await (await UploadAsync(Client, OggVoice, "note.ogg", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.FileTypeNotAllowed);
        var video = await (await UploadAsync(Client, WebmVoice, "clip.webm", superAdmin.AccessToken)).ReadAsync<FileResponse>();
        (video.Kind, video.MimeType).ShouldBe((FileKind.Video, "video/webm"));
    }

    [Fact]
    public async Task A_voice_note_over_10_MB_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        byte[] atTheLimit = [.. WebmVoice, .. new byte[10 * 1024 * 1024 - WebmVoice.Length]];

        (await UploadVoiceNoteAsync(Client, superAdmin.AccessToken, atTheLimit)).SizeBytes.ShouldBe(atTheLimit.Length);
        var tooBig = await UploadAsync(Client, [.. atTheLimit, 0], "long.webm", superAdmin.AccessToken, "/api/files/voice");
        await tooBig.ShouldFailWithAsync(422, ErrorCodes.FileTooLarge);
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
        var seen = await Client.GetAsync($"/api/files/{photo.Id}", colleague.AccessToken);
        seen.StatusCode.ShouldBe(HttpStatusCode.OK);
        // the browser keeps a profile photo: everyone signed in may see it
        seen.Headers.CacheControl!.Private.ShouldBeTrue();
        seen.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromDays(1));
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
        var read = await Client.GetAsync($"/api/files/{shared.Id}", ben.AccessToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        // never kept by the browser: the next person signed in there might not see it
        read.Headers.CacheControl!.NoStore.ShouldBeTrue();
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

    [Fact]
    public async Task A_daily_logs_voice_note_is_played_by_its_writer_and_the_readers_of_every_log_only()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id, layla.User.Id]);

        var memo = await UploadVoiceNoteAsync(Client, rami.AccessToken);
        var logged = await Client.PostJsonAsync($"/api/projects/{site.Id}/site-logs", new { body = "", voiceFileId = memo.Id }, rami.AccessToken);
        ((int)logged.StatusCode).ShouldBe(201, await logged.Content.ReadAsStringAsync());
        (await logged.ReadAsync<SiteLogResponse>()).VoiceNote!.Id.ShouldBe(memo.Id);

        // the writer, and whoever reads every log of Execution
        foreach (var reader in new[] { rami, siteAdmin, superAdmin })
        {
            var played = await Client.GetAsync($"/api/files/{memo.Id}", reader.AccessToken);
            played.StatusCode.ShouldBe(HttpStatusCode.OK);
            played.Content.Headers.ContentType!.MediaType.ShouldBe("audio/webm");
            played.Headers.CacheControl!.NoStore.ShouldBeTrue();
            (await played.Content.ReadAsByteArrayAsync()).ShouldBe(WebmVoice);
        }

        // a colleague on the same site, Design's admin and a designer: as if it didn't exist
        foreach (var other in new[] { layla, designAdmin, designer })
        {
            await (await Client.GetAsync($"/api/files/{memo.Id}", other.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        }

        // attached once: neither another log nor a chat message can take it
        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/site-logs", new { body = "Again", voiceFileId = memo.Id }, rami.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileInUse);
        await (await Client.PostJsonAsync($"/api/messages/direct/{superAdmin.User.Id}", new { body = "", attachmentIds = new[] { memo.Id } }, rami.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.FileInUse);
    }

    [Fact]
    public async Task A_voice_note_on_a_log_is_never_cleaned_up_until_the_log_lets_it_go()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var site = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution, staffIds: [rami.User.Id]);
        var first = await UploadVoiceNoteAsync(Client, rami.AccessToken);
        var second = await UploadVoiceNoteAsync(Client, rami.AccessToken);
        var neverUsed = await UploadVoiceNoteAsync(Client, rami.AccessToken);
        var firstKey = await _steps.ScalarAsync<string>("SELECT storage_key FROM files WHERE id = @id", ("@id", first.Id));
        var logged = await Client.PostJsonAsync($"/api/projects/{site.Id}/site-logs", new { body = "", voiceFileId = first.Id }, rami.AccessToken);
        var log = await logged.ReadAsync<SiteLogResponse>();
        // a day and more old; the second recording is aged only once it is on the log, or the first clean-up would take it
        Task AgeAsync(long id) => _steps.ExecuteAsync("UPDATE files SET created_at = DATE_SUB(UTC_TIMESTAMP(), INTERVAL 2 DAY) WHERE id = @id", ("@id", id));
        await AgeAsync(first.Id);
        await AgeAsync(neverUsed.Id);

        async Task CleanUpAsync()
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IFileService>().DeleteUnusedAsync();
        }

        async Task AssertPlayableAsync(long id, bool playable)
        {
            var response = await Client.GetAsync($"/api/files/{id}", rami.AccessToken);
            if (playable)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
            }
            else
            {
                await response.ShouldFailWithAsync(404, ErrorCodes.NotFound);
            }
        }

        // a day and more old, but it belongs to a log: kept, while a recording attached to nothing goes
        await CleanUpAsync();
        await AssertPlayableAsync(first.Id, true);
        File.Exists(Path.Combine(api.FilesRoot, firstKey)).ShouldBeTrue();
        await AssertPlayableAsync(neverUsed.Id, false);

        // replaced: the old one is attached to nothing now, so it goes; the new one stays
        (await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "", voiceFileId = second.Id }, rami.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await AgeAsync(second.Id);
        await CleanUpAsync();
        await AssertPlayableAsync(first.Id, false);
        File.Exists(Path.Combine(api.FilesRoot, firstKey)).ShouldBeFalse();
        await AssertPlayableAsync(second.Id, true);

        // removed (the words keep the log valid): it goes too
        (await Client.PutJsonAsync($"/api/site-logs/{log.Id}", new { body = "Slab poured.", voiceFileId = (long?)null }, rami.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await CleanUpAsync();
        await AssertPlayableAsync(second.Id, false);
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
