using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Invitations;

/// <summary>What the inviter decides. The invitee fills in their own name, phone and password later.</summary>
public sealed record CreateInvitationRequest
{
    public string Email { get; init; } = string.Empty;

    /// <summary>super_admin, admin or employee.</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>design or execution. Must be empty for super admins; admins may omit it (their own domain).</summary>
    public string? DomainCode { get; init; }

    public long? TeamId { get; init; }

    public string? Position { get; init; }

    /// <summary>Required for admins and super admins, who are paid a fixed monthly salary; optional for employees.</summary>
    public InvitationPayRequest? Pay { get; init; }
}

public sealed record InvitationPayRequest
{
    public PayBasis Basis { get; init; }

    /// <summary>Hourly rate or monthly salary, depending on <see cref="Basis"/>.</summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// What an hour of a salary's overtime is paid at, before the multiplier. Leave it empty for the salary ÷ 184. Hourly
    /// pay is its own overtime rate, so it takes none.
    /// </summary>
    public decimal? OvertimeRate { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Defaults to today in the company time zone.</summary>
    public DateOnly? EffectiveFrom { get; init; }
}
