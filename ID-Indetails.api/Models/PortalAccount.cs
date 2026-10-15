using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A client's sign-in to the client portal. Kept apart from staff <see cref="User"/>s: one account per email address,
/// which can be given access to several Design projects (<see cref="PortalProjectAccess"/>).
/// </summary>
public class PortalAccount
{
    public long Id { get; set; }

    /// <summary>Normalised (trimmed, lower case) and unique among portal accounts.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Set by the client when they accept their invitation.</summary>
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>Null until the invitation is accepted.</summary>
    public string? PasswordHash { get; set; }

    public PortalAccountStatus Status { get; set; }

    /// <summary>Access tokens issued before this instant are refused (a password reset or the last project's access ended).</summary>
    public DateTime? SessionsValidFrom { get; set; }

    /// <summary>Wrong passwords in a row; reset by a successful sign-in.</summary>
    public int FailedSignIns { get; set; }

    /// <summary>Sign-in is refused until then after too many wrong passwords.</summary>
    public DateTime? LockedUntil { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The staff member whose invitation created the account.</summary>
    public long CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
