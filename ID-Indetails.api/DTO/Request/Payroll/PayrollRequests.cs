using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Payroll;

public sealed record SalaryListQuery
{
    /// <summary>YYYY-MM; this month when empty. Never a future month.</summary>
    public string? Month { get; init; }

    public string? Domain { get; init; }

    /// <summary>Matches name and email.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>A page of the caller's hours ledger: 12 months, newest first.</summary>
public sealed record MyHoursQuery
{
    public int Page { get; init; } = 1;
}

/// <summary>The day whose hours the caller's schedule shows.</summary>
public sealed record MyScheduleQuery
{
    /// <summary>Today when empty.</summary>
    public DateOnly? Date { get; init; }
}

public sealed record SalaryMonthQuery
{
    /// <summary>YYYY-MM; this month when empty.</summary>
    public string? Month { get; init; }
}

public sealed record AddHourExcuseRequest
{
    /// <summary>YYYY-MM.</summary>
    public string Month { get; init; } = string.Empty;

    public ExcuseKind Kind { get; init; }

    /// <summary>Only for <see cref="ExcuseKind.Minutes"/>.</summary>
    public int? Minutes { get; init; }

    /// <summary>Only for <see cref="ExcuseKind.Days"/>: days of that month.</summary>
    public IReadOnlyList<DateOnly> Days { get; init; } = [];

    public bool IsPaid { get; init; } = true;

    public string? Note { get; init; }
}
