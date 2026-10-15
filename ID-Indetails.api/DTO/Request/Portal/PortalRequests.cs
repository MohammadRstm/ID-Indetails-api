namespace ID_Indetails.api.DTO.Request.Portal;

/// <summary>Invites one of the project client's email contacts to the client portal.</summary>
public sealed record InvitePortalClientRequest
{
    public string Email { get; init; } = string.Empty;
}

public sealed record PortalInvitationTokenRequest
{
    public string Token { get; init; } = string.Empty;
}

/// <summary>Accepts a portal invitation: the client's name and the password they choose.</summary>
public sealed record AcceptPortalInvitationRequest
{
    public string Token { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
