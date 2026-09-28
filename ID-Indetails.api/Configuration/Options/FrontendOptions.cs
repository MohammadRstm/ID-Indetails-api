using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Where the React client lives, for links placed in emails.</summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    [Required, Url]
    public string BaseUrl { get; init; } = "http://localhost:5173";

    /// <summary>Signup page for invited users; the token is appended as <c>?token=</c>.</summary>
    [Required]
    public string AcceptInvitationPath { get; init; } = "/accept-invitation";

    [Required]
    public string LoginPath { get; init; } = "/login";

    public string AcceptInvitationUrl(string token)
        => $"{BaseUrl.TrimEnd('/')}{AcceptInvitationPath}?token={Uri.EscapeDataString(token)}";

    public string LoginUrl => $"{BaseUrl.TrimEnd('/')}{LoginPath}";
}
