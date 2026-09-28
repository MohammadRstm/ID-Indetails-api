namespace ID_Indetails.api.DTO.Request.Billing;

/// <summary>Replaces the domain's billing settings, creating them the first time.</summary>
public sealed record SaveBillingSettingsRequest
{
    /// <summary>Start of every invoice number, e.g. <c>HL</c> gives HL-2026-001.</summary>
    public string InvoicePrefix { get; init; } = string.Empty;

    public int DefaultTermsDays { get; init; } = 30;

    public string? BankAccount { get; init; }

    public string? BankName { get; init; }

    public string? Iban { get; init; }

    public string? Swift { get; init; }

    public string? TermsText { get; init; }

    public string? Footer { get; init; }
}

public sealed record ExchangeRateListQuery
{
    /// <summary>ISO code, e.g. LBP. Empty for every currency.</summary>
    public string? Currency { get; init; }
}
