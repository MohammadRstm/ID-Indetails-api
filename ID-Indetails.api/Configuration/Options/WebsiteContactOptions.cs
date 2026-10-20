namespace ID_Indetails.api.Configuration.Options;

/// <summary>Where the website's contact form goes while the company has set no contact email of its own (docs/website.md).</summary>
public sealed class WebsiteContactOptions
{
    public const string SectionName = "WebsiteContact";

    /// <summary>Never shown on the website. Empty: with no contact email set, the form answers 503.</summary>
    public string FallbackAddress { get; init; } = string.Empty;
}
