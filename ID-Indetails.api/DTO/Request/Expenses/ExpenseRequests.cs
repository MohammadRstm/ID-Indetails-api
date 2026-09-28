namespace ID_Indetails.api.DTO.Request.Expenses;

public sealed record CreateExpenseRequest
{
    public string Label { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>assets, services, contractor, software, travel, hardware or other (the default).</summary>
    public string? Category { get; init; }

    /// <summary>Defaults to today (company time zone).</summary>
    public DateOnly? SpentOn { get; init; }

    public string? Note { get; init; }
}
