using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

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
}
