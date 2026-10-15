using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.ContentStore.Implementation;

/// <summary>
/// A private Cloudflare R2 bucket, through its S3 API. The bytes go straight between the browser and R2 on presigned
/// links; the API only starts, lists, completes and deletes. Network failures and 5xx answers become a 503, with the
/// cause logged here (the 503 itself is logged without it).
/// </summary>
public sealed class R2ContentStore : IContentStore, IDisposable
{
    private const string NoSuchUpload = "NoSuchUpload";

    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly IClock _clock;
    private readonly ILogger<R2ContentStore> _logger;

    public R2ContentStore(IOptions<ContentStoreOptions> options, IClock clock, ILogger<R2ContentStore> logger)
    {
        var r2 = options.Value.R2;
        _bucket = r2.Bucket;
        _clock = clock;
        _logger = logger;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = r2.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                // AWSSDK 4 otherwise signs checksum parameters into the links, which neither R2 nor a browser PUT can satisfy.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public bool IsAvailable => true;

    public async Task<string> StartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            ct => _client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = _bucket, Key = key, ContentType = contentType }, ct),
            cancellationToken);
        return response.UploadId;
    }

    public Task<string> GetPartUploadUrlAsync(string key, string uploadId, int partNumber, DateTime expiresAt, CancellationToken cancellationToken = default)
        => _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            UploadId = uploadId,
            PartNumber = partNumber,
            Expires = AsUtc(expiresAt),
            Protocol = Protocol.HTTPS,
        });

    public async Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        var parts = new List<StoredPart>();
        string? marker = null;
        try
        {
            while (true)
            {
                var request = new ListPartsRequest { BucketName = _bucket, Key = key, UploadId = uploadId, PartNumberMarker = marker };
                var page = await SendAsync(ct => _client.ListPartsAsync(request, ct), cancellationToken);
                parts.AddRange((page.Parts ?? []).Select(p => new StoredPart(p.PartNumber ?? 0, p.Size ?? 0, p.ETag)));
                if (page.IsTruncated != true)
                {
                    return parts;
                }

                marker = page.NextPartNumberMarker?.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == NoSuchUpload)
        {
            return null;
        }
    }

    public async Task CompleteUploadAsync(string key, string uploadId, IReadOnlyList<StoredPart> parts, CancellationToken cancellationToken = default)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            UploadId = uploadId,
            PartETags = parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, p.ETag)).ToList(),
        };
        try
        {
            await SendAsync(ct => _client.CompleteMultipartUploadAsync(request, ct), cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == NoSuchUpload)
        {
            // joined by another attempt a moment ago, or abandoned: whether the object exists tells which
        }
    }

    public async Task AbortUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(ct => _client.AbortMultipartUploadAsync(_bucket, key, uploadId, ct), cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == NoSuchUpload)
        {
            // already completed or aborted
        }
    }

    public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await SendAsync(ct => _client.GetObjectMetadataAsync(_bucket, key, ct), cancellationToken);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<byte[]> ReadStartAsync(string key, int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        try
        {
            var request = new GetObjectRequest { BucketName = _bucket, Key = key, ByteRange = new ByteRange(0, count - 1) };
            using var response = await SendAsync(ct => _client.GetObjectAsync(request, ct), cancellationToken);
            var buffer = new byte[count];
            // an object shorter than the range comes back whole
            var read = await response.ResponseStream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
            return buffer[..read];
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // an empty object has no first byte to ask for
            return [];
        }
    }

    public Task<string> GetDownloadUrlAsync(
        string key, string fileName, string contentType, bool inline, DateTime expiresAt, CancellationToken cancellationToken = default)
        => _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = AsUtc(expiresAt),
            Protocol = Protocol.HTTPS,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = contentType,
                ContentDisposition = ContentDisposition(fileName, inline),
                CacheControl = ContentLinks.CacheControl(expiresAt, _clock.UtcNow),
            },
        });

    /// <summary>Deleting a key that isn't there succeeds on S3 and R2 alike, so a missing object needs no special case.</summary>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => await SendAsync(ct => _client.DeleteObjectAsync(_bucket, key, ct), cancellationToken);

    public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? token = null;
        do
        {
            var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix, ContinuationToken = token };
            var page = await SendAsync(ct => _client.ListObjectsV2Async(request, ct), cancellationToken);
            foreach (var item in page.S3Objects ?? [])
            {
                yield return new StoredObject(item.Key, item.Size ?? 0, AsUtc(item.LastModified ?? default));
            }

            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (token is not null);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>
    /// <c>inline</c> or <c>attachment</c>, with the name twice: an ASCII fallback for old clients, and the real one as
    /// RFC 5987 <c>filename*</c>. Control characters, quotes and backslashes can't reach the header.
    /// </summary>
    public static string ContentDisposition(string fileName, bool inline)
    {
        var ascii = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            ascii.Append(c is >= ' ' and <= '~' and not ('"' or '\\' or '%') ? c : '_');
        }

        var fallback = ascii.Length == 0 ? "file" : ascii.ToString();
        return $"{(inline ? "inline" : "attachment")}; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    private async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (Exception ex) when (IsOutage(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "The content store couldn't be reached, or failed to answer.");
            throw new ServiceUnavailableException(
                ErrorCodes.ContentStorageUnavailable, "The file storage can't be reached right now. Try again in a moment.", ex);
        }
    }

    /// <summary>No answer at all, or an answer of 500 or more. Other errors (a bad request, a refusal) are ours to fix, not an outage.</summary>
    private static bool IsOutage(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        // a timeout, unless the caller itself gave up
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        AmazonServiceException service => service.StatusCode == 0 || (int)service.StatusCode >= 500,
        HttpRequestException or WebException or IOException => true,
        _ => false,
    };

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
