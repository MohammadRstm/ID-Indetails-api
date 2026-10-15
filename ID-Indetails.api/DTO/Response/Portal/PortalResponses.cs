using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Portal;

/// <summary>
/// Returned when a client signs in, refreshes or accepts an invitation. The refresh token is never in the body: it
/// travels only in the portal's HttpOnly cookie.
/// </summary>
public sealed record PortalAuthResponse
{
    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAt { get; init; }

    public required PortalAccountResponse Account { get; init; }
}

/// <summary>The signed-in client and the projects they may open.</summary>
public sealed record PortalAccountResponse
{
    public required long Id { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required IReadOnlyList<PortalProjectResponse> Projects { get; init; }
}

/// <summary>A project the client may open, as their project list shows it.</summary>
public sealed record PortalProjectResponse
{
    public required long Id { get; init; }

    public required string Title { get; init; }

    public required ProjectStatus Status { get; init; }

    /// <summary>Unread chat messages and comments on the project.</summary>
    public required int Unread { get; init; }

    /// <summary>A short-lived link to the first published hero image, or null.</summary>
    public required string? HeroUrl { get; init; }

    public required DateTime? HeroUrlExpiresAt { get; init; }

    /// <summary>The project's start date.</summary>
    public required DateOnly StartedOn { get; init; }

    /// <summary>The last publication; null before the first.</summary>
    public required DateTime? LastPublishedAt { get; init; }

    /// <summary>The latest of the last publication, the newest chat message and the newest comment (deleted ones aside).</summary>
    public required DateTime? LastActivityAt { get; init; }

    /// <summary>Published since the client last opened the project (or published at all, if they never did), or anything unread.</summary>
    public required bool HasNews { get; init; }
}

/// <summary>What an invitation link opens, shown before the client sets their password.</summary>
public sealed record PortalInvitationPreviewResponse
{
    public required string Email { get; init; }

    public required string ProjectTitle { get; init; }

    public required DateTime ExpiresAt { get; init; }
}

/// <summary>The project client's email contacts and where each stands on the client portal.</summary>
public sealed record PortalClientsResponse
{
    public required long ProjectId { get; init; }

    /// <summary>Why no one can be invited now (an internal or archived project, an archived client), or null.</summary>
    public string? CannotInviteReason { get; init; }

    public required IReadOnlyList<PortalClientResponse> Clients { get; init; }
}

/// <summary>One email address: a contact of the project's client, or someone who had access and is no longer a contact.</summary>
public sealed record PortalClientResponse
{
    public required string Email { get; init; }

    /// <summary>The contact's label, such as "Office".</summary>
    public string? Label { get; init; }

    /// <summary>False when the address is no longer among the client's contacts: it can't be invited again.</summary>
    public required bool IsContact { get; init; }

    public required PortalClientStatus Status { get; init; }

    public long? AccountId { get; init; }

    /// <summary>The name the client gave when they accepted.</summary>
    public string? Name { get; init; }

    public long? InvitationId { get; init; }

    public DateTime? InvitedAt { get; init; }

    public DateTime? InvitationExpiresAt { get; init; }

    public long? AccessId { get; init; }

    public DateTime? AccessGrantedAt { get; init; }

    public DateTime? AccessEndedAt { get; init; }

    public DateTime? LastSeenAt { get; init; }
}
