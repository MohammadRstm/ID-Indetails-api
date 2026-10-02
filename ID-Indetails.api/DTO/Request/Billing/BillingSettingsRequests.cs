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

    /// <summary>Email clients reminders of their overdue invoices.</summary>
    public bool RemindersEnabled { get; init; }

    /// <summary>1 to 60 days after the due date, the first reminder.</summary>
    public int ReminderFirstAfterDays { get; init; } = 1;

    /// <summary>1 to 60 days from one reminder to the next.</summary>
    public int ReminderEveryDays { get; init; } = 7;

    /// <summary>1 to 10 reminders an invoice gets at most.</summary>
    public int ReminderMax { get; init; } = 3;
}

public sealed record AddExchangeRateRequest
{
    /// <summary>ISO code of a currency other than USD, e.g. LBP.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>How many units of the currency one US dollar buys.</summary>
    public decimal RatePerUsd { get; init; }

    /// <summary>Today or earlier, after the currency's latest rate. Defaults to today.</summary>
    public DateOnly? EffectiveFrom { get; init; }
}

public sealed record ExchangeRateListQuery
{
    /// <summary>ISO code, e.g. LBP. Empty for every currency.</summary>
    public string? Currency { get; init; }
}
