using System.Text;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Implementation;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.ContentStore;

/// <summary>The local store's links and its multipart uploads, on a temporary folder.</summary>
public sealed class LocalContentStoreTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private const string Key = "portal/12/3f2a9c4e";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"idi_test_content_{Guid.NewGuid():N}");
    private readonly Mock<IClock> _clock = new();
    private DateTime _now = Start;

    public LocalContentStoreTests() => _clock.SetupGet(c => c.UtcNow).Returns(() => _now);

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private LocalContentStore NewStore(string signingKey = "first-test-signing-key")
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(_contentRoot);
        var options = Options.Create(new ContentStoreOptions
        {
            Provider = ContentStoreProvider.Local,
            Local = new LocalContentStoreOptions { RootPath = "store", SigningKey = signingKey },
        });
        return new LocalContentStore(options, environment.Object, _clock.Object);
    }

    private static string TokenOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    private static byte[] Bytes(int length, int seed) => Enumerable.Range(0, length).Select(i => (byte)(seed + i)).ToArray();

    /// <summary>Starts an upload and returns its id with a link for part 1.</summary>
    private async Task<(string UploadId, string Token)> StartedAsync(LocalContentStore store)
    {
        var uploadId = await store.StartUploadAsync(Key, "application/pdf");
        return (uploadId, TokenOf(await store.GetPartUploadUrlAsync(Key, uploadId, 1, _now.AddMinutes(15))));
    }

    private async Task UploadAsync(LocalContentStore store, string key, params byte[][] parts)
    {
        var uploadId = await store.StartUploadAsync(key, "application/pdf");
        for (var i = 0; i < parts.Length; i++)
        {
            var url = await store.GetPartUploadUrlAsync(key, uploadId, i + 1, _now.AddMinutes(15));
            (await store.WritePartAsync(TokenOf(url), new MemoryStream(parts[i]), parts[i].Length)).ShouldNotBeNull();
        }

        await store.CompleteUploadAsync(key, uploadId, (await store.ListPartsAsync(key, uploadId)).ShouldNotBeNull());
    }

    private static async Task<List<StoredObject>> ListAsync(LocalContentStore store, string prefix)
    {
        var found = new List<StoredObject>();
        await foreach (var item in store.ListAsync(prefix))
        {
            found.Add(item);
        }

        return found;
    }

    [Fact]
    public async Task A_valid_part_link_is_relative_and_saves_the_part_it_was_made_for()
    {
        var store = NewStore();
        var (uploadId, token) = await StartedAsync(store);
        var url = await store.GetPartUploadUrlAsync(Key, uploadId, 2, _now.AddMinutes(15));

        url.ShouldStartWith("/api/content-store/parts/");
        var part = (await store.WritePartAsync(TokenOf(url), new MemoryStream(Bytes(10, 1)), 10)).ShouldNotBeNull();

        part.PartNumber.ShouldBe(2);
        part.Size.ShouldBe(10);
        (await store.WritePartAsync(token, new MemoryStream(Bytes(5, 1)), 5)).ShouldNotBeNull().PartNumber.ShouldBe(1);
    }

    [Fact]
    public async Task A_link_with_a_changed_payload_or_signature_is_refused()
    {
        var store = NewStore();
        var (_, token) = await StartedAsync(store);

        var dot = token.IndexOf('.');
        var json = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token[..dot]));
        var otherPart = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json.Replace("\"Part\":1", "\"Part\":2"))) + token[dot..];
        var otherSignature = token[..(dot + 1)] + (token[dot + 1] == 'A' ? 'B' : 'A') + token[(dot + 2)..];

        otherPart.ShouldNotBe(token);
        (await store.WritePartAsync(otherPart, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        (await store.WritePartAsync(otherSignature, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        (await store.WritePartAsync(token, new MemoryStream(Bytes(3, 1)), 3)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    public async Task Something_that_is_not_a_token_is_refused_without_an_error(string token)
    {
        var store = NewStore();

        (await store.WritePartAsync(token, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        store.ResolveDownload(token).ShouldBeNull();
    }

    [Fact]
    public async Task A_link_stops_working_when_it_expires()
    {
        var store = NewStore();
        var (_, token) = await StartedAsync(store);

        _now = Start.AddMinutes(14);
        (await store.WritePartAsync(token, new MemoryStream(Bytes(3, 1)), 3)).ShouldNotBeNull();

        _now = Start.AddMinutes(15);
        (await store.WritePartAsync(token, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
    }

    [Fact]
    public async Task A_link_for_one_operation_does_not_work_for_the_other()
    {
        var store = NewStore();
        await UploadAsync(store, Key, Bytes(20, 1));
        var (_, putToken) = await StartedAsync(store);
        var getToken = TokenOf(await store.GetDownloadUrlAsync(Key, "plan.pdf", "application/pdf", inline: true, _now.AddMinutes(5)));

        store.ResolveDownload(getToken).ShouldNotBeNull();
        (await store.WritePartAsync(putToken, new MemoryStream(Bytes(3, 1)), 3)).ShouldNotBeNull();
        (await store.WritePartAsync(getToken, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        store.ResolveDownload(putToken).ShouldBeNull();
    }

    [Fact]
    public async Task A_link_signed_with_another_key_is_refused()
    {
        var first = NewStore("first-test-signing-key");
        var second = NewStore("second-test-signing-key");
        await UploadAsync(first, Key, Bytes(20, 1));
        var (_, putToken) = await StartedAsync(first);
        var getToken = TokenOf(await first.GetDownloadUrlAsync(Key, "plan.pdf", "application/pdf", inline: true, _now.AddMinutes(5)));

        // the same folder, so only the signature differs
        (await second.WritePartAsync(putToken, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        second.ResolveDownload(getToken).ShouldBeNull();
        first.ResolveDownload(getToken).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_upload_in_parts_becomes_one_object()
    {
        var store = NewStore();
        var first = Bytes(1000, 1);
        var second = Bytes(400, 7);
        var uploadId = await store.StartUploadAsync(Key, "application/pdf");

        // sent out of order: the object joins them by part number
        foreach (var (number, bytes) in new[] { (2, second), (1, first) })
        {
            var url = await store.GetPartUploadUrlAsync(Key, uploadId, number, _now.AddMinutes(15));
            var written = (await store.WritePartAsync(TokenOf(url), new MemoryStream(bytes), bytes.Length)).ShouldNotBeNull();
            written.PartNumber.ShouldBe(number);
            written.Size.ShouldBe(bytes.Length);
        }

        var parts = (await store.ListPartsAsync(Key, uploadId)).ShouldNotBeNull();
        parts.Select(p => p.PartNumber).ShouldBe([1, 2]);
        parts.Select(p => p.Size).ShouldBe([1000L, 400L]);
        (await store.GetSizeAsync(Key)).ShouldBeNull();

        await store.CompleteUploadAsync(Key, uploadId, parts);

        var all = first.Concat(second).ToArray();
        (await store.GetSizeAsync(Key)).ShouldBe(1400);
        (await store.ReadStartAsync(Key, 10)).ShouldBe(all[..10]);
        (await store.ReadStartAsync(Key, 5000)).ShouldBe(all);
        (await store.ListPartsAsync(Key, uploadId)).ShouldBeNull();
        Directory.Exists(Path.Combine(_contentRoot, "store", ".uploads", uploadId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_part_sent_again_replaces_the_first_try()
    {
        var store = NewStore();
        var (uploadId, token) = await StartedAsync(store);

        await store.WritePartAsync(token, new MemoryStream(Bytes(50, 1)), 50);
        await store.WritePartAsync(token, new MemoryStream(Bytes(30, 1)), 30);

        var parts = (await store.ListPartsAsync(Key, uploadId)).ShouldNotBeNull();
        parts.Count.ShouldBe(1);
        parts[0].Size.ShouldBe(30);
    }

    [Fact]
    public async Task A_download_link_resolves_to_the_file_with_its_name_and_type()
    {
        var store = NewStore();
        var data = Bytes(300, 3);
        await UploadAsync(store, Key, data);

        var url = await store.GetDownloadUrlAsync(Key, "Plan – v2.pdf", "application/pdf", inline: false, _now.AddMinutes(5));
        var download = store.ResolveDownload(TokenOf(url)).ShouldNotBeNull();

        url.ShouldStartWith("/api/content-store/objects/");
        download.FileName.ShouldBe("Plan – v2.pdf");
        download.ContentType.ShouldBe("application/pdf");
        download.Inline.ShouldBeFalse();
        File.ReadAllBytes(download.Path).ShouldBe(data);
        download.CacheControl.ShouldBe("private, max-age=300");

        // served later, it may be kept only for what the link has left
        _now = Start.AddMinutes(4);
        store.ResolveDownload(TokenOf(url)).ShouldNotBeNull().CacheControl.ShouldBe("private, max-age=60");
    }

    [Fact]
    public async Task Completing_an_upload_that_is_already_joined_is_not_an_error()
    {
        var store = NewStore();
        var (uploadId, token) = await StartedAsync(store);
        await store.WritePartAsync(token, new MemoryStream(Bytes(40, 1)), 40);
        var parts = (await store.ListPartsAsync(Key, uploadId)).ShouldNotBeNull();
        await store.CompleteUploadAsync(Key, uploadId, parts);

        await store.CompleteUploadAsync(Key, uploadId, parts);

        (await store.GetSizeAsync(Key)).ShouldBe(40);
    }

    [Fact]
    public async Task A_deleted_object_is_gone_and_deleting_it_again_is_fine()
    {
        var store = NewStore();
        await UploadAsync(store, Key, Bytes(20, 1));
        var token = TokenOf(await store.GetDownloadUrlAsync(Key, "plan.pdf", "application/pdf", inline: true, _now.AddMinutes(5)));

        await store.DeleteAsync(Key);
        await store.DeleteAsync(Key);

        (await store.GetSizeAsync(Key)).ShouldBeNull();
        store.ResolveDownload(token).ShouldBeNull();
    }

    [Fact]
    public async Task An_aborted_upload_is_removed_and_aborting_it_again_is_fine()
    {
        var store = NewStore();
        var (uploadId, token) = await StartedAsync(store);

        await store.AbortUploadAsync(Key, uploadId);
        await store.AbortUploadAsync(Key, uploadId);

        (await store.ListPartsAsync(Key, uploadId)).ShouldBeNull();
        (await store.WritePartAsync(token, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
    }

    [Fact]
    public async Task Listing_walks_the_objects_by_prefix_and_skips_uploads_in_progress()
    {
        var store = NewStore();
        await UploadAsync(store, "portal/1/aaa", Bytes(10, 1));
        await UploadAsync(store, "portal/2/bbb", Bytes(20, 1));
        var (_, token) = await StartedAsync(store);
        await store.WritePartAsync(token, new MemoryStream(Bytes(5, 1)), 5);

        var one = await ListAsync(store, "portal/1/");
        var all = await ListAsync(store, "");

        one.Select(o => o.Key).ShouldBe(["portal/1/aaa"]);
        one[0].Size.ShouldBe(10);
        one[0].LastModifiedUtc.Kind.ShouldBe(DateTimeKind.Utc);
        all.Select(o => o.Key).OrderBy(k => k).ShouldBe(["portal/1/aaa", "portal/2/bbb"]);
    }

    [Fact]
    public async Task A_listing_of_a_store_nothing_was_written_to_is_empty()
        => (await ListAsync(NewStore(), "")).ShouldBeEmpty();

    [Fact]
    public async Task A_part_larger_than_the_link_allows_is_refused_and_not_kept()
    {
        var store = NewStore();
        var (uploadId, token) = await StartedAsync(store);

        var written = await store.WritePartAsync(token, new MemoryStream(Bytes(5, 1)), LocalContentStore.MaxPartBytes + 1);

        written.ShouldBeNull();
        (await store.ListPartsAsync(Key, uploadId)).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_part_for_an_unknown_upload_is_refused()
    {
        var store = NewStore();
        var (uploadId, _) = await StartedAsync(store);
        var unknown = TokenOf(await store.GetPartUploadUrlAsync(Key, Guid.NewGuid().ToString("N"), 1, _now.AddMinutes(15)));
        var notAnId = TokenOf(await store.GetPartUploadUrlAsync(Key, "../../outside", 1, _now.AddMinutes(15)));
        var otherKey = TokenOf(await store.GetPartUploadUrlAsync("portal/99/other", uploadId, 1, _now.AddMinutes(15)));

        (await store.WritePartAsync(unknown, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        (await store.WritePartAsync(notAnId, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        (await store.WritePartAsync(otherKey, new MemoryStream(Bytes(3, 1)), 3)).ShouldBeNull();
        (await store.ListPartsAsync(Key, Guid.NewGuid().ToString("N"))).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(LocalContentStore.MaxParts + 1)]
    public async Task A_part_number_outside_what_R2_accepts_gets_no_link(int partNumber)
    {
        var store = NewStore();
        var (uploadId, _) = await StartedAsync(store);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => store.GetPartUploadUrlAsync(Key, uploadId, partNumber, _now.AddMinutes(15)));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("portal/../../escape")]
    [InlineData("")]
    public async Task A_key_that_leaves_the_root_is_refused(string key)
    {
        var store = NewStore();

        await Should.ThrowAsync<InvalidOperationException>(() => store.StartUploadAsync(key, "text/plain"));
        await Should.ThrowAsync<InvalidOperationException>(() => store.GetSizeAsync(key));
        await Should.ThrowAsync<InvalidOperationException>(() => store.DeleteAsync(key));
    }
}
