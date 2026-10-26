namespace ID_Indetails.api.DTO.Response.Auth;

public sealed record CurrentUserResponse
{
    public required long Id { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>super_admin, admin or employee.</summary>
    public required string Role { get; init; }

    /// <summary>design, execution or website; null for super admins.</summary>
    public string? Domain { get; init; }

    public TeamSummaryResponse? Team { get; init; }

    public string? Position { get; init; }

    public string? Phone { get; init; }

    /// <summary>The day they accepted their invitation; null until then.</summary>
    public DateOnly? JoinedOn { get; init; }

    /// <summary>Their profile photo, read through <c>GET /api/files/{id}</c>.</summary>
    public long? PhotoFileId { get; init; }

    /// <summary>True until a generated password is replaced. Every other endpoint refuses until then.</summary>
    public required bool MustChangePassword { get; init; }

    /// <summary>
    /// Effective permission codes (role defaults plus personal overrides), for showing and hiding
    /// UI. The server still checks every call.
    /// </summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>True when an admin or employee has been given or denied permissions beyond their role defaults.</summary>
    public required bool IsJoker { get; init; }

    /// <summary>Domains the user may enter: their own, plus any other once they hold one of its permissions ("website" opens the Website area).</summary>
    public required IReadOnlyList<string> VisibleDomains { get; init; }

    /// <summary>Whether Proton suggests replies to them in the discussions with clients: their own choice.</summary>
    public required bool ProtonReplySuggestions { get; init; }
}

public sealed record TeamSummaryResponse(long Id, string Name);
