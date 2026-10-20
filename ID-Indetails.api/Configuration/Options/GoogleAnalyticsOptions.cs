using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// The company website's Google Analytics 4 property, read with the Data API as a service account with Viewer access
/// (docs/website.md, "Analytics"). Off unless switched on with a property and a key. The key comes from user secrets
/// locally and the <c>GoogleAnalytics__ServiceAccountKeyBase64</c> environment variable in production.
/// </summary>
public sealed class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    public bool Enabled { get; init; }

    /// <summary>The property's number (GA Admin, Property details), not the website's <c>G-</c> measurement ID.</summary>
    public string PropertyId { get; init; } = string.Empty;

    /// <summary>The service account's JSON key, base64-encoded so it fits on one line of the server's <c>.env</c>.</summary>
    public string ServiceAccountKeyBase64 { get; init; } = string.Empty;

    /// <summary>How long one call to Google may take.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; init; } = 10;
}
