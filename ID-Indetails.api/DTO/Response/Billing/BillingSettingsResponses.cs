namespace ID_Indetails.api.DTO.Response.Billing;

public sealed record BillingSettingsResponse(
    string Domain,
    string InvoicePrefix,
    int DefaultTermsDays,
    string? BankAccount,
    string? BankName,
    string? Iban,
    string? Swift,
    string? TermsText,
    string? Footer,
    DateTime? UpdatedAt);

/// <summary>How many units of <see cref="Currency"/> one US dollar buys, from <see cref="EffectiveFrom"/>.</summary>
public sealed record ExchangeRateResponse(long Id, string Currency, decimal RatePerUsd, DateOnly EffectiveFrom, DateTime CreatedAt);
