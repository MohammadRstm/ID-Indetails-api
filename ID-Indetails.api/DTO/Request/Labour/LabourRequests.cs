namespace ID_Indetails.api.DTO.Request.Labour;

public sealed record LabourDayRequest
{
    public DateOnly Date { get; init; }

    public int Workers { get; init; }

    /// <summary>Pay per worker for the day, in USD.</summary>
    public decimal DailyPay { get; init; }
}

/// <summary>Submits a week, or replaces the days of one already reported.</summary>
public sealed record SubmitLabourWeekRequest
{
    public IReadOnlyList<LabourDayRequest> Days { get; init; } = [];

    public string? Note { get; init; }
}

/// <summary>The crew size and day rate new weeks start from, applying from today.</summary>
public sealed record SetLabourDefaultsRequest
{
    public int Workers { get; init; }

    public decimal DailyPay { get; init; }
}
