using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Billing;

public sealed record PaymentListQuery
{
    public long? ProjectId { get; init; }

    public long? ClientId { get; init; }

    public long? InvoiceId { get; init; }

    /// <summary>When empty, voided payments are left out.</summary>
    public bool? IncludeVoided { get; init; }

    /// <summary>Must be a domain the caller can see, or nothing is returned.</summary>
    public string? Domain { get; init; }

    /// <summary>Only payments received on or after this day.</summary>
    public DateOnly? ReceivedFrom { get; init; }

    /// <summary>Only payments received on or before this day.</summary>
    public DateOnly? ReceivedTo { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record RecordPaymentRequest
{
    public decimal Amount { get; init; }

    /// <summary>USD or LBP.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Units of the currency per US dollar. Defaults to the dated rate in force on the day received.</summary>
    public decimal? FxRate { get; init; }

    /// <summary>Today or earlier. Defaults to today.</summary>
    public DateOnly? ReceivedOn { get; init; }

    public PaymentMethod Method { get; init; } = PaymentMethod.Bank;

    public string? Reference { get; init; }

    public string? Note { get; init; }

    /// <summary>An issued invoice of the project. Empty for a payment on account.</summary>
    public long? InvoiceId { get; init; }

    /// <summary>The project's initial payment. At most one live one per project.</summary>
    public bool IsInitial { get; init; }

    /// <summary>One of the company's account labels, to file the payment under. Empty for none.</summary>
    public long? AccountLabelId { get; init; }
}

public sealed record ApplyPaymentRequest
{
    public long InvoiceId { get; init; }
}

/// <summary>Files a payment under an account label, or clears it.</summary>
public sealed record SetPaymentLabelRequest
{
    /// <summary>The label to file it under; empty clears it.</summary>
    public long? AccountLabelId { get; init; }
}
