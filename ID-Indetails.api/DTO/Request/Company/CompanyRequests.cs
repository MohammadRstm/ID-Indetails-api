using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Company;

public sealed record UpdateCompanySettingsRequest
{
    public string Name { get; init; } = string.Empty;

    /// <summary>For the letterhead of invoices and statements; empty clears it.</summary>
    public string? Address { get; init; }

    /// <summary>A web address such as indetails.com; empty clears it.</summary>
    public string? Website { get; init; }

    /// <summary>0 = Sunday, 1 = Monday.</summary>
    public byte WeekStart { get; init; } = 1;

    public bool Use24HourTime { get; init; }

    /// <summary>USD or LBP.</summary>
    public string DefaultCurrency { get; init; } = "USD";
}

/// <summary>A new company working week, from <see cref="EffectiveFrom"/> (today when empty).</summary>
public sealed record SetWorkingTimeRequest
{
    public DateOnly? EffectiveFrom { get; init; }

    /// <summary>Weekdays, 0 = Sunday … 6 = Saturday.</summary>
    public IReadOnlyList<int> WorkDays { get; init; } = [];

    /// <summary>Minutes after midnight, e.g. 540 = 09:00.</summary>
    public int StartMinute { get; init; }

    public int EndMinute { get; init; }

    public int BreakMinutes { get; init; }

    public decimal OvertimeMultiplier { get; init; } = 1m;
}

/// <summary>A person's own working week from <see cref="EffectiveFrom"/>, or back to the company week.</summary>
public sealed record SetScheduleRequest
{
    public DateOnly? EffectiveFrom { get; init; }

    public bool FollowsDefault { get; init; }

    /// <summary>Working days only; a weekday not listed is a day off. Empty when following the default.</summary>
    public IReadOnlyList<ScheduleDayRequest> Days { get; init; } = [];
}

public sealed record ScheduleDayRequest(int Weekday, int StartMinute, int EndMinute);

public sealed record ScheduleListQuery
{
    public string? Domain { get; init; }

    /// <summary>Matches name, position and team.</summary>
    public string? Search { get; init; }
}

public sealed record CreateOverheadRequest
{
    public string Name { get; init; } = string.Empty;

    public OverheadCostGroup CostGroup { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Per month.</summary>
    public decimal Amount { get; init; }

    /// <summary>First month charged (any day in it); this month when empty. Never a past month.</summary>
    public DateOnly? StartMonth { get; init; }
}

public sealed record UpdateOverheadRequest
{
    public string Name { get; init; } = string.Empty;

    public OverheadCostGroup CostGroup { get; init; }
}

public sealed record ChangeOverheadAmountRequest
{
    public decimal Amount { get; init; }

    /// <summary>First month at the new amount (any day in it); this month when empty.</summary>
    public DateOnly? FromMonth { get; init; }
}

public sealed record ChangeOverheadCurrencyRequest
{
    public string Currency { get; init; } = string.Empty;

    /// <summary>The monthly amount in the new currency.</summary>
    public decimal Amount { get; init; }

    /// <summary>First month in the new currency (any day in it); this month when empty.</summary>
    public DateOnly? FromMonth { get; init; }
}
