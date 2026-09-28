namespace ID_Indetails.api.DTO.Request.Invitations;

/// <summary>The token from the emailed link. Sent in the body, not the URL, to keep it out of access logs.</summary>
public sealed record InvitationTokenRequest
{
    public string Token { get; init; } = string.Empty;
}
