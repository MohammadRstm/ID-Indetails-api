using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Employees;

/// <summary>A row of the people list.</summary>
public record EmployeeSummaryResponse
{
    public required long Id { get; init; }

    public required string Email { get; init; }

    /// <summary>Empty until an invited person signs up.</summary>
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public required string Role { get; init; }

    /// <summary>Null for super admins.</summary>
    public string? Domain { get; init; }

    public TeamSummaryResponse? Team { get; init; }

    public string? Position { get; init; }

    /// <summary>Their profile photo, read through <c>GET /api/files/{id}</c>.</summary>
    public long? PhotoFileId { get; init; }

    public required UserStatus Status { get; init; }

    /// <summary>True when their permissions differ from their role defaults.</summary>
    public required bool IsJoker { get; init; }

    public DateOnly? JoinedOn { get; init; }

    /// <summary>The last working day of someone who left; null while they work here.</summary>
    public DateOnly? LeftOn { get; init; }

    public DateTime? LastSeenAt { get; init; }

    /// <summary>Whether they have the live connection open now.</summary>
    public bool Online { get; init; }

    /// <summary>When a super admin ended their sessions; they can't sign in until it is restored. Null otherwise.</summary>
    public DateTime? SessionsRevokedAt { get; init; }
}

/// <summary>A person's profile.</summary>
public sealed record EmployeeResponse : EmployeeSummaryResponse
{
    public string? Phone { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>The pay in force today. Only for callers who may see financials or manage pay.</summary>
    public PayResponse? CurrentPay { get; init; }
}

/// <summary>One dated pay change.</summary>
public sealed record PayResponse(
    long Id,
    DateOnly EffectiveFrom,
    PayBasis Basis,
    decimal Amount,
    string Currency,
    decimal HourlyCostRate,
    string? Note,
    DateTime CreatedAt,
    long? CreatedById);
