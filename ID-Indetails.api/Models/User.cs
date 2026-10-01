using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// Everyone who can sign in. The row is created when a person is invited
/// (<see cref="UserStatus.Invited"/>, no password or name yet); accepting the invitation
/// fills in their details and makes them <see cref="UserStatus.Active"/>.
/// </summary>
public class User
{
    public long Id { get; set; }

    /// <summary>Sign-in identity. Unique across the whole company.</summary>
    public string Email { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Phone { get; set; }

    public string RoleCode { get; set; } = string.Empty;

    /// <summary>Home domain. Null only for super admins.</summary>
    public string? DomainCode { get; set; }

    public long? TeamId { get; set; }

    public string? Position { get; set; }

    public long? PhotoFileId { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Invited;

    /// <summary>
    /// When the account was deactivated; cleared on reactivation. Pay and the hours ledger end on that day, and past
    /// months keep the person.
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// When a super admin ended all their sessions; set until a super admin restores their sign-in. While it is set,
    /// every request, refresh and sign-in of theirs is refused.
    /// </summary>
    public DateTime? SessionsRevokedAt { get; set; }

    /// <summary>
    /// Access tokens issued before this are refused for good, so a token copied before their sessions were ended
    /// doesn't work again once sign-in is restored. Kept after the restore.
    /// </summary>
    public DateTime? SessionsValidFrom { get; set; }

    /// <summary>Set for accounts created with a generated password (the first super admin).</summary>
    public bool MustChangePassword { get; set; }

    public DateOnly? JoinedOn { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Who invited this person. Null for accounts created by the developer tool.</summary>
    public long? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Role Role { get; set; } = null!;

    public Domain? Domain { get; set; }

    public Team? Team { get; set; }

    public StoredFile? PhotoFile { get; set; }

    public ICollection<UserPayHistory> PayHistory { get; set; } = new List<UserPayHistory>();

    public ICollection<EmploymentPeriod> EmploymentPeriods { get; set; } = new List<EmploymentPeriod>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<UserInvitation> Invitations { get; set; } = new List<UserInvitation>();

    /// <summary>Personal differences from the role defaults (see <see cref="UserPermissionOverride"/>).</summary>
    public ICollection<UserPermissionOverride> PermissionOverrides { get; set; } = new List<UserPermissionOverride>();
}
