using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Projects;

/// <summary>A department on a project, by name.</summary>
public sealed record ProjectDepartmentRefResponse(long Id, string Name);

/// <summary>
/// A Design project's departments with their planned hours; their shares and money only for callers with
/// <c>financial:view</c>, their logged hours and progress also for those with <c>work_entry:view_all</c>.
/// </summary>
public sealed record ProjectDepartmentsResponse
{
    /// <summary>"Full file" when every Design department works on it; null otherwise.</summary>
    public string? Name { get; init; }

    public required IReadOnlyList<ProjectDepartmentResponse> Departments { get; init; }

    /// <summary>
    /// Hours logged by people in no department, or in one no longer on the project, with their cost: they belong to
    /// no budget. Null when there are none, or without <c>financial:view</c>.
    /// </summary>
    public DepartmentMoneyResponse? Other { get; init; }
}

public sealed record ProjectDepartmentResponse
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Its part of the price, a fraction (0.25 is 25%). Null without <c>financial:view</c>.</summary>
    public decimal? Share { get; init; }

    /// <summary>Null without <c>financial:view</c>.</summary>
    public DepartmentMoneyResponse? Money { get; init; }

    /// <summary>The hours planned for it, in minutes; null while it has no plan. Shown to whoever sees the departments.</summary>
    public int? PlannedMinutes { get; init; }

    /// <summary>
    /// The minutes logged in it on the project. Null for a caller who sees neither the money (<c>financial:view</c>) nor
    /// everyone's hours (<c>work_entry:view_all</c>): a department may be one person, whose hours are theirs.
    /// </summary>
    public int? LoggedMinutes { get; init; }

    /// <summary>
    /// Logged ÷ planned minutes, a fraction to four places (0.5 is half); above 1 once more is logged than planned. Null
    /// without a plan, or when <see cref="LoggedMinutes"/> is.
    /// </summary>
    public decimal? Progress { get; init; }
}

/// <summary>A department's budget and labour, in USD: its share of the price before TVA, and the cost of the hours logged in it.</summary>
public sealed record DepartmentMoneyResponse
{
    public decimal Budget { get; init; }

    public decimal Cost { get; init; }

    /// <summary>Budget less cost; below zero when the department is over its budget.</summary>
    public decimal Left { get; init; }

    public int Minutes { get; init; }

    /// <summary>Labour logged in it with no exchange rate on its day, per currency: left out of the cost and what is left, never counted as 0.</summary>
    public required IReadOnlyList<CurrencyAmountResponse> Unconverted { get; init; }

    /// <summary>Who logged hours in it, most cost first.</summary>
    public required IReadOnlyList<DepartmentPersonResponse> People { get; init; }
}

public sealed record DepartmentPersonResponse
{
    public required PersonRefResponse Person { get; init; }

    public int Minutes { get; init; }

    public decimal Cost { get; init; }

    /// <summary>Month by month, oldest first.</summary>
    public required IReadOnlyList<DepartmentMonthResponse> Months { get; init; }
}

/// <param name="Month">Its first day.</param>
/// <param name="HourlyRate">Their hourly cost that month, in USD; null when it isn't known.</param>
/// <param name="Cost">Paid breaks included.</param>
public sealed record DepartmentMonthResponse(DateOnly Month, int Minutes, decimal? HourlyRate, decimal Cost);

/// <summary>A change of a project's split: the departments and shares before and after, and why.</summary>
public sealed record ProjectDepartmentChangeResponse
{
    public long Id { get; init; }

    public ProjectDepartmentChangeKind Kind { get; init; }

    /// <summary>Empty when the project was created.</summary>
    public required IReadOnlyList<DepartmentShareResponse> Before { get; init; }

    public required IReadOnlyList<DepartmentShareResponse> After { get; init; }

    public string? Reason { get; init; }

    public required PersonRefResponse ChangedBy { get; init; }

    public DateTime ChangedAt { get; init; }
}

/// <param name="Name">As it was named then.</param>
public sealed record DepartmentShareResponse(long Id, string Name, decimal Share);
