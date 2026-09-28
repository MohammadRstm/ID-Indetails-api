namespace ID_Indetails.api.DTO.Response.Invitations;

/// <summary>What the signup page shows before the invitee fills in their details.</summary>
public sealed record InvitationPreviewResponse
{
    public required string Email { get; init; }

    public required string Role { get; init; }

    public string? Domain { get; init; }

    public string? Team { get; init; }

    public string? Position { get; init; }

    public required DateTime ExpiresAt { get; init; }
}
