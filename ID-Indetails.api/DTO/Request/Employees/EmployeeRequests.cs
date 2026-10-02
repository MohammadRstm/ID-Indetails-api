using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Employees;

/// <summary>Filters for the people list. Everything is optional.</summary>
public sealed record EmployeeListQuery
{
    /// <summary>Matches first name, last name, email and position; every word must match.</summary>
    public string? Search { get; init; }

    public long? TeamId { get; init; }

    /// <summary>super_admin, admin or employee.</summary>
    public string? Role { get; init; }

    /// <summary>Home domain. Must be one the caller can see, or nothing is returned.</summary>
    public string? Domain { get; init; }

    /// <summary>When empty, deactivated accounts are left out.</summary>
    public UserStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>The profile fields an admin maintains. All of them are replaced.</summary>
public sealed record UpdateEmployeeRequest
{
    /// <summary>Required once the person has signed up; may stay empty while they are invited.</summary>
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Phone { get; init; }

    public string? Position { get; init; }

    /// <summary>A team of the person's domain, or null for none.</summary>
    public long? TeamId { get; init; }
}

public sealed record ChangeRoleRequest
{
    /// <summary>super_admin, admin or employee.</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Only when a super admin becomes an admin or employee: the domain they join.</summary>
    public string? DomainCode { get; init; }
}

public sealed record MoveEmployeeRequest
{
    public string DomainCode { get; init; } = string.Empty;

    /// <summary>A team of the new domain, or null for none.</summary>
    public long? TeamId { get; init; }
}

public sealed record ChangePayRequest
{
    public PayBasis Basis { get; init; }

    /// <summary>Hourly rate or monthly salary, depending on <see cref="Basis"/>.</summary>
    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Defaults to today in the company time zone. Never in the future, and after the latest change.</summary>
    public DateOnly? EffectiveFrom { get; init; }

    public string? Note { get; init; }
}

/// <summary>What people may change about themselves.</summary>
public sealed record UpdateMyProfileRequest
{
    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string? Phone { get; init; }
}

/// <summary>A new sign-in address, set by a super admin.</summary>
public sealed record ChangeEmailRequest
{
    public string Email { get; init; } = string.Empty;
}

/// <summary>A super admin's own new sign-in address, confirmed with their password.</summary>
public sealed record ChangeMyEmailRequest
{
    public string Email { get; init; } = string.Empty;

    public string CurrentPassword { get; init; } = string.Empty;
}

public sealed record DeactivateEmployeeRequest
{
    /// <summary>Their last day of work, paid; today when empty.</summary>
    public DateOnly? LastWorkingDay { get; init; }
}

public sealed record ReactivateEmployeeRequest
{
    /// <summary>
    /// Whether their time away is paid. Asked, and required, only when they left earlier this month; otherwise time
    /// away is never paid and this must be left out.
    /// </summary>
    public bool? PayGap { get; init; }
}
