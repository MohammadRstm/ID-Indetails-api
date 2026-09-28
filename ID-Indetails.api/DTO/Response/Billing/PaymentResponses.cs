using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Billing;

public sealed record PaymentResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public InvoiceProjectResponse Project { get; init; } = null!;

    public InvoiceClientResponse Client { get; init; } = null!;

    /// <summary>Empty for a payment on account.</summary>
    public long? InvoiceId { get; init; }

    public string? InvoiceNumber { get; init; }

    public bool IsInitial { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Units of <see cref="Currency"/> per US dollar used for this payment.</summary>
    public decimal FxRate { get; init; }

    public decimal AmountUsd { get; init; }

    public DateOnly ReceivedOn { get; init; }

    public PaymentMethod Method { get; init; }

    public string? Reference { get; init; }

    public string? Note { get; init; }

    public DateTime RecordedAt { get; init; }

    public DateTime? VoidedAt { get; init; }

    public string? VoidReason { get; init; }
}
