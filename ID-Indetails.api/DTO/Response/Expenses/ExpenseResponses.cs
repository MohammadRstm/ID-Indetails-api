namespace ID_Indetails.api.DTO.Response.Expenses;

public sealed record ExpenseResponse(
    long Id,
    string Label,
    decimal Amount,
    string Currency,
    string Category,
    DateOnly SpentOn,
    string? Note,
    DateTime CreatedAt);

public sealed record CurrencyTotalResponse(string Currency, decimal Total);

/// <summary>Newest first, with a total for each currency.</summary>
public sealed record ExpenseListResponse(IReadOnlyList<ExpenseResponse> Items, IReadOnlyList<CurrencyTotalResponse> Totals);
