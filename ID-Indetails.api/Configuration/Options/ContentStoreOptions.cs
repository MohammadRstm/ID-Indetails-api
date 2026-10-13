using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Where client portal files are kept (docs/content-storage.md). Validated in <c>AddContentStore</c>.</summary>
public sealed class ContentStoreOptions
{
    public const string SectionName = "ContentStore";

    public ContentStoreProvider Provider { get; init; } = ContentStoreProvider.None;

    /// <summary>Used when <see cref="Provider"/> is R2.</summary>
    public R2ContentStoreOptions R2 { get; init; } = new();

    /// <summary>Used when <see cref="Provider"/> is Local.</summary>
    public LocalContentStoreOptions Local { get; init; } = new();
}

public sealed class R2ContentStoreOptions
{
    public string AccountId { get; init; } = string.Empty;

    /// <summary>A private bucket: no public access and no custom domain.</summary>
    public string Bucket { get; init; } = string.Empty;

    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>A secret: user secrets locally, the <c>ContentStore__R2__SecretAccessKey</c> environment variable in production.</summary>
    public string SecretAccessKey { get; init; } = string.Empty;

    /// <summary>Overrides <c>https://{AccountId}.r2.cloudflarestorage.com</c>. Signed links only work on that S3 endpoint.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>The endpoint the store talks to and signs links for.</summary>
    public string ServiceUrl => string.IsNullOrWhiteSpace(Endpoint) ? $"https://{AccountId}.r2.cloudflarestorage.com" : Endpoint.TrimEnd('/');
}

public sealed class LocalContentStoreOptions
{
    /// <summary>A folder on disk, relative to the app's content root unless absolute. Never a publicly served folder.</summary>
    public string RootPath { get; init; } = "App_Data/portal-content";

    /// <summary>Signs the store's links. Empty, a random key is made on each start, so links die with the process.</summary>
    public string SigningKey { get; init; } = string.Empty;
}
