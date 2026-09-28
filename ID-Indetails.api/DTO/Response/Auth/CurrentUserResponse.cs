namespace ID_Indetails.api.DTO.Response.Auth;

public sealed record CurrentUserResponse
{
    public required long Id { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>super_admin, admin or employee.</summary>
    public required string Role { get; init; }

    /// <summary>design or execution; null for super admins.</summary>
    public string? Domain { get; init; }

    public TeamSummaryResponse? Team { get; init; }

    public string? Position { get; init; }

    /// <summary>True until a generated password is replaced. Every other endpoint refuses until then.</summary>
    public required bool MustChangePassword { get; init; }

    /// <summary>Effective permission codes, for showing and hiding UI. The server still checks every call.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }
}

public sealed record TeamSummaryResponse(long Id, string Name);
