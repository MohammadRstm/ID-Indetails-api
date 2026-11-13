using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Payroll;

/// <summary>Someone asking for part of their pay early, in the currency of their pay.</summary>
public sealed record AskSalaryAdvanceRequest
{
    public decimal Amount { get; init; }

    /// <summary>Why: required, and seen only by the person and the super admins.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>What an advance takes from the person's pay in one month.</summary>
/// <param name="Month">The first day of the month.</param>
public sealed record PlannedMonthRequest(DateOnly Month, decimal Amount);

public sealed record ApproveSalaryAdvanceRequest
{
    /// <summary>What is given: what was asked for when empty, and never more.</summary>
    public decimal? Amount { get; init; }

    /// <summary>The months it comes off, from this month on, adding up to what is given.</summary>
    public IReadOnlyList<PlannedMonthRequest> Months { get; init; } = [];

    public string? Note { get; init; }
}

/// <summary>A super admin recording an advance they handed over, without a request.</summary>
public sealed record RecordSalaryAdvanceRequest
{
    public long UserId { get; init; }

    public decimal Amount { get; init; }

    /// <summary>The day it was handed over: from the first of this month up to today, today when empty.</summary>
    public DateOnly? GivenOn { get; init; }

    public IReadOnlyList<PlannedMonthRequest> Months { get; init; } = [];

    public string? Note { get; init; }
}

/// <summary>The months still open (this month on), adding up to what the closed months don't plan to take.</summary>
public sealed record SalaryAdvanceMonthsRequest
{
    public IReadOnlyList<PlannedMonthRequest> Months { get; init; } = [];
}

/// <summary>Declining a request or cancelling an advance: the note is optional.</summary>
public sealed record SalaryAdvanceNoteRequest
{
    public string? Note { get; init; }
}

/// <summary>Closing what is left: repaid in cash, or written off as a company expense today.</summary>
public sealed record SettleSalaryAdvanceRequest
{
    public SalaryAdvanceSettlement Kind { get; init; }

    public string? Note { get; init; }
}

public sealed record SalaryAdvanceListQuery
{
    public SalaryAdvanceView View { get; init; } = SalaryAdvanceView.Pending;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
