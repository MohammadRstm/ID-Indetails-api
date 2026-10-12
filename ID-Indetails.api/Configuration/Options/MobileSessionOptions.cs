using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>The phone app's sessions (docs/authentication.md, "Mobile sessions"). The website keeps <see cref="RefreshTokenOptions"/>.</summary>
public sealed class MobileSessionOptions
{
    public const string SectionName = "MobileSession";

    /// <summary>Each refresh moves the session's end to this many days ahead, so a phone used once a month stays signed in.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 30;

    /// <summary>How long after a rotation the phone may present the old token again because the answer was lost.</summary>
    [Range(0, 900)]
    public int ReplayGraceSeconds { get; init; } = 300;
}
