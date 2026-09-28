namespace ID_Indetails.api.DTO.Request.Invitations;

public sealed record AcceptInvitationRequest
{
    public string Token { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string? Phone { get; init; }

    public string Password { get; init; } = string.Empty;
}
