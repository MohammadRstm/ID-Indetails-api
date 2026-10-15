using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.ContentStore.Implementation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Shouldly;

namespace ID_Indetails.UnitTests.ContentStore;

/// <summary>Presigned links are made without any network call, so they are checked with fake credentials.</summary>
public sealed class R2ContentStoreTests : IDisposable
{
    private const string AccountId = "0123456789abcdef0123456789abcdef";
    private const string Bucket = "portal-test";
    private const string Key = "portal/12/3f2a9c4e";

    private readonly R2ContentStore _store = new(Options.Create(new ContentStoreOptions
    {
        Provider = ContentStoreProvider.R2,
        R2 = new R2ContentStoreOptions
        {
            AccountId = AccountId,
            Bucket = Bucket,
            AccessKeyId = "test-access-key-id",
            SecretAccessKey = "test-secret-access-key-not-real",
        },
    }));

    public void Dispose() => _store.Dispose();

    private static Dictionary<string, StringValues> QueryOf(Uri uri) => QueryHelpers.ParseQuery(uri.Query);

    [Fact]
    public async Task A_part_link_is_a_presigned_PUT_on_the_account_endpoint_signed_for_the_host_only()
    {
        var url = new Uri(await _store.GetPartUploadUrlAsync(Key, "upload-123", 3, DateTime.UtcNow.AddMinutes(15)));
        var query = QueryOf(url);

        url.Scheme.ShouldBe("https");
        url.Host.ShouldBe($"{AccountId}.r2.cloudflarestorage.com");
        url.AbsolutePath.ShouldBe($"/{Bucket}/{Key}");
        query["partNumber"].ToString().ShouldBe("3");
        query["uploadId"].ToString().ShouldBe("upload-123");
        int.Parse(query["X-Amz-Expires"].ToString()).ShouldBeInRange(800, 900);
        query.ContainsKey("X-Amz-Signature").ShouldBeTrue();
        // a browser's plain PUT carries no header but its host
        query["X-Amz-SignedHeaders"].ToString().ShouldBe("host");
    }

    [Fact]
    public async Task A_link_asks_for_no_checksum_that_R2_or_a_browser_could_not_give()
    {
        var part = QueryOf(new Uri(await _store.GetPartUploadUrlAsync(Key, "upload-123", 1, DateTime.UtcNow.AddMinutes(15))));
        var download = QueryOf(new Uri(await _store.GetDownloadUrlAsync(Key, "plan.pdf", "application/pdf", inline: true, DateTime.UtcNow.AddMinutes(5))));

        part.Keys.Concat(download.Keys)
            .Where(k => k.StartsWith("x-amz-checksum", StringComparison.OrdinalIgnoreCase) || k.StartsWith("x-amz-sdk-checksum", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_download_link_overrides_the_type_and_the_disposition()
    {
        var url = new Uri(await _store.GetDownloadUrlAsync(Key, "Plan – v2.pdf", "application/pdf", inline: false, DateTime.UtcNow.AddMinutes(5)));
        var query = QueryOf(url);

        url.Host.ShouldBe($"{AccountId}.r2.cloudflarestorage.com");
        url.AbsolutePath.ShouldBe($"/{Bucket}/{Key}");
        query["response-content-type"].ToString().ShouldBe("application/pdf");
        query["response-content-disposition"].ToString().ShouldBe("attachment; filename=\"Plan _ v2.pdf\"; filename*=UTF-8''Plan%20%E2%80%93%20v2.pdf");
        query.ContainsKey("partNumber").ShouldBeFalse();
    }

    [Theory]
    [InlineData("plan.pdf", true, "inline; filename=\"plan.pdf\"; filename*=UTF-8''plan.pdf")]
    [InlineData("Plan – v2.pdf", false, "attachment; filename=\"Plan _ v2.pdf\"; filename*=UTF-8''Plan%20%E2%80%93%20v2.pdf")]
    [InlineData("say \"hi\".txt", true, "inline; filename=\"say _hi_.txt\"; filename*=UTF-8''say%20%22hi%22.txt")]
    [InlineData("a\r\nb;c\\d%e", true, "inline; filename=\"a__b;c_d_e\"; filename*=UTF-8''a%0D%0Ab%3Bc%5Cd%25e")]
    [InlineData("", false, "attachment; filename=\"file\"; filename*=UTF-8''")]
    public void The_disposition_names_the_file_in_ascii_and_in_utf8_without_header_tricks(string fileName, bool inline, string expected)
        => R2ContentStore.ContentDisposition(fileName, inline).ShouldBe(expected);

    [Fact]
    public void A_custom_endpoint_is_used_instead_of_the_account_one()
    {
        var options = new R2ContentStoreOptions { AccountId = AccountId, Endpoint = "https://storage.example.test/" };

        options.ServiceUrl.ShouldBe("https://storage.example.test");
        new R2ContentStoreOptions { AccountId = AccountId }.ServiceUrl.ShouldBe($"https://{AccountId}.r2.cloudflarestorage.com");
    }
}
