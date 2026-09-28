namespace ID_Indetails.api.RateLimiting;

/// <summary>Names of the rate-limit policies used with <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>Endpoints that take passwords or tokens: slows down guessing.</summary>
    public const string Auth = "auth";

    /// <summary>The developer-only setup endpoint.</summary>
    public const string Setup = "setup";

    /// <summary>File exports: heavier than a normal read, limited per user.</summary>
    public const string Export = "export";
}
