namespace ID_Indetails.api.Enums;

/// <summary>Where client portal content is kept (<c>ContentStore:Provider</c>, docs/content-storage.md).</summary>
public enum ContentStoreProvider
{
    /// <summary>Not set up: anything that needs the store answers 503 until it is.</summary>
    None,

    /// <summary>A folder on disk. Development and tests only; production refuses to start with it.</summary>
    Local,

    /// <summary>A private Cloudflare R2 bucket.</summary>
    R2,
}
