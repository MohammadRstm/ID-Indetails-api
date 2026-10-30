namespace ID_Indetails.api.RateLimiting;

/// <summary>Names of the rate-limit policies used with <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>Endpoints that take passwords or tokens: slows down guessing.</summary>
    public const string Auth = "auth";

    /// <summary>Session refreshes: per IP, roomier than <see cref="Auth"/>, since the refresh token itself is the secret.</summary>
    public const string Refresh = "refresh";

    /// <summary>A signed-in person's own password checks (change password or sign-in address): per user.</summary>
    public const string Credential = "credential";

    /// <summary>Requests that email an address the caller chooses (invoices, quotes, BOQs, invitations): per user.</summary>
    public const string Email = "email";

    /// <summary>The developer-only setup endpoint.</summary>
    public const string Setup = "setup";

    /// <summary>Heavy exports (CSV): per signed-in user.</summary>
    public const string Export = "export";

    /// <summary>File uploads: per signed-in user.</summary>
    public const string Upload = "upload";

    /// <summary>Bug reports, which send an email each: per signed-in user, per hour.</summary>
    public const string BugReport = "bug-report";

    /// <summary>Questions to Proton, which call a paid model: per signed-in user.</summary>
    public const string Proton = "proton";

    /// <summary>A client's chat messages and comments, per portal account.</summary>
    public const string PortalWrite = "portal-write";

    /// <summary>The public website's contact form, which emails the company: per IP, per hour.</summary>
    public const string WebsiteContact = "website-contact";

    /// <summary>The Website area's analytics, each new period of which asks Google Analytics: per signed-in user.</summary>
    public const string WebsiteAnalytics = "website-analytics";
}

/// <summary>Where the public website's API lives: no sign-in, read-only apart from the contact form.</summary>
public static class PublicWebsitePaths
{
    public const string Prefix = "/api/public/website";

    /// <summary>The address a website picture is served at.</summary>
    public static string File(long fileId) => $"{Prefix}/files/{fileId}";
}
