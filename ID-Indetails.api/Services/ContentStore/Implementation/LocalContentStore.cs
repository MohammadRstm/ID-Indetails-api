using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.ContentStore.Implementation;

/// <summary>
/// A folder on disk, for development and tests only (production refuses it). Objects sit at <c>{root}/{key}</c>, an upload
/// in progress at <c>{root}/.uploads/{uploadId}/</c>. With no S3 to presign for it, the store hands out relative links
/// signed with an HMAC, which <c>LocalContentStoreController</c> serves by calling <see cref="WritePartAsync"/> and <see cref="ResolveDownload"/>.
/// </summary>
public sealed class LocalContentStore(IOptions<ContentStoreOptions> options, IHostEnvironment environment, IClock clock) : IContentStore
{
    public const string PartLinkPrefix = "/api/content-store/parts/";
    public const string ObjectLinkPrefix = "/api/content-store/objects/";

    /// <summary>The largest part a link accepts, as on R2.</summary>
    public const long MaxPartBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>The most parts an upload can have, as on R2.</summary>
    public const int MaxParts = 10_000;

    private const string UploadsFolder = ".uploads";
    private const string MetaFile = "meta.json";

    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Local.RootPath)));
    private readonly LocalLinkSigner _links = new(options.Value.Local.SigningKey);

    private string UploadsRoot => Path.Combine(_root, UploadsFolder);

    public bool IsAvailable => true;

    public async Task<string> StartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default)
    {
        _ = PathOf(key);
        var uploadId = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(UploadsRoot, uploadId);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(
            Path.Combine(folder, MetaFile), JsonSerializer.SerializeToUtf8Bytes(new UploadMeta(key, contentType)), cancellationToken);
        return uploadId;
    }

    public Task<string> GetPartUploadUrlAsync(string key, string uploadId, int partNumber, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(partNumber, MaxParts);
        var link = new LocalLink(LocalLinkSigner.PutOperation, key, LocalLinkSigner.ToUnixSeconds(expiresAt), uploadId, partNumber, MaxPartBytes);
        return Task.FromResult(PartLinkPrefix + _links.Sign(link));
    }

    /// <summary>
    /// Saves the body as the part the link is for, replacing an earlier try. Null when the link is invalid or expired, the
    /// upload is unknown, or the body is larger than the link allows; nothing is kept then.
    /// </summary>
    public async Task<StoredPart?> WritePartAsync(string token, Stream body, long? contentLength, CancellationToken cancellationToken = default)
    {
        var link = _links.Verify(token, LocalLinkSigner.PutOperation, clock.UtcNow);
        if (link?.UploadId is null || contentLength > link.Max)
        {
            return null;
        }

        var folder = await FindUploadAsync(link.Key, link.UploadId, cancellationToken);
        if (folder is null)
        {
            return null;
        }

        // written beside the parts under another name, so a half-sent part is never listed or joined
        var temp = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
        try
        {
            long size;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var copied = await CopyAtMostAsync(body, file, link.Max, cancellationToken);
                if (copied is null)
                {
                    return null;
                }

                size = copied.Value;
            }

            File.Move(temp, Path.Combine(folder, PartFile(link.Part)), overwrite: true);
            return new StoredPart(link.Part, size, ETagOf(link.Part, size));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public async Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        var folder = await FindUploadAsync(key, uploadId, cancellationToken);
        if (folder is null)
        {
            return null;
        }

        return new DirectoryInfo(folder).EnumerateFiles("*.part")
            .Select(file =>
            {
                var number = int.Parse(Path.GetFileNameWithoutExtension(file.Name), CultureInfo.InvariantCulture);
                return new StoredPart(number, file.Length, ETagOf(number, file.Length));
            })
            .OrderBy(part => part.PartNumber)
            .ToList();
    }

    public async Task CompleteUploadAsync(string key, string uploadId, IReadOnlyList<StoredPart> parts, CancellationToken cancellationToken = default)
    {
        var folder = await FindUploadAsync(key, uploadId, cancellationToken)
            ?? throw new InvalidOperationException("There is no such upload to complete.");
        var target = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var joined = Path.Combine(folder, "joined.tmp");
        await using (var output = new FileStream(joined, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var part in parts.OrderBy(p => p.PartNumber))
            {
                await using var input = new FileStream(Path.Combine(folder, PartFile(part.PartNumber)), FileMode.Open, FileAccess.Read, FileShare.Read);
                await input.CopyToAsync(output, cancellationToken);
            }
        }

        File.Move(joined, target, overwrite: true);
        Directory.Delete(folder, recursive: true);
    }

    public async Task AbortUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        var folder = await FindUploadAsync(key, uploadId, cancellationToken);
        if (folder is not null)
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(PathOf(key));
        return Task.FromResult<long?>(file.Exists ? file.Length : null);
    }

    /// <summary>A missing object throws <see cref="FileNotFoundException"/>: callers check <see cref="GetSizeAsync"/> first.</summary>
    public async Task<byte[]> ReadStartAsync(string key, int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        await using var file = new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[(int)Math.Min(count, file.Length)];
        var read = await file.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        return buffer[..read];
    }

    public Task<string> GetDownloadUrlAsync(
        string key, string fileName, string contentType, bool inline, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        _ = PathOf(key);
        var link = new LocalLink(
            LocalLinkSigner.GetOperation, key, LocalLinkSigner.ToUnixSeconds(expiresAt), FileName: fileName, ContentType: contentType, Inline: inline);
        return Task.FromResult(ObjectLinkPrefix + _links.Sign(link));
    }

    /// <summary>The file a download link is for, or null when the token is invalid, expired, or its file is gone.</summary>
    public LocalDownload? ResolveDownload(string token)
    {
        var link = _links.Verify(token, LocalLinkSigner.GetOperation, clock.UtcNow);
        if (link?.FileName is null || link.ContentType is null)
        {
            return null;
        }

        var path = PathOf(link.Key);
        return File.Exists(path) ? new LocalDownload(path, link.FileName, link.ContentType, link.Inline) : null;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken = default)
        => Objects(prefix).ToAsyncEnumerable();

    private IEnumerable<StoredObject> Objects(string prefix)
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var key = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (key.StartsWith(UploadsFolder + "/", StringComparison.Ordinal) || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var file = new FileInfo(path);
            yield return new StoredObject(key, file.Length, file.LastWriteTimeUtc);
        }
    }

    /// <summary>The upload's folder, or null when there is no such upload for this key.</summary>
    private async Task<string?> FindUploadAsync(string key, string uploadId, CancellationToken cancellationToken)
    {
        // 32 hex digits, so nothing in an upload id can point outside the uploads folder
        if (!Guid.TryParseExact(uploadId, "N", out _))
        {
            return null;
        }

        var folder = Path.Combine(UploadsRoot, uploadId);
        var metaPath = Path.Combine(folder, MetaFile);
        if (!File.Exists(metaPath))
        {
            return null;
        }

        var meta = JsonSerializer.Deserialize<UploadMeta>(await File.ReadAllBytesAsync(metaPath, cancellationToken));
        return meta?.Key == key ? folder : null;
    }

    /// <summary>Copies the body, or returns null as soon as it is longer than <paramref name="max"/> bytes.</summary>
    private static async Task<long?> CopyAtMostAsync(Stream from, Stream to, long max, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > max)
            {
                return null;
            }

            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    /// <summary>Keys are ours, but a key that would leave the root is refused anyway.</summary>
    private string PathOf(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException("A storage key pointed outside the content store root.");
    }

    private static string PartFile(int partNumber) => $"{partNumber:D5}.part";

    private static string ETagOf(int partNumber, long size) => $"\"{size}-{partNumber}\"";

    private sealed record UploadMeta(string Key, string ContentType);
}

/// <summary>A file a signed download link points at, for the controller to stream.</summary>
public sealed record LocalDownload(string Path, string FileName, string ContentType, bool Inline);
