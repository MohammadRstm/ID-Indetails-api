namespace ID_Indetails.api.DTO.Response.Invitations;

/// <summary>Returned to the inviter after an invitation is created or resent.</summary>
public sealed record InvitationResponse
{
    public required long UserId { get; init; }

    public required string Email { get; init; }

    public required string Role { get; init; }

    public string? Domain { get; init; }

    public required DateTime ExpiresAt { get; init; }
}
