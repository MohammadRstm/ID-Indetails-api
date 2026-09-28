using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Billing;

public sealed record InvoiceProjectResponse(long Id, string Title);

public sealed record InvoiceClientResponse(long Id, string DisplayName);

public sealed record InvoiceLineResponse(long Id, int Position, string Description, decimal Amount);

public sealed record InvoiceDeliveryResponse(long Id, DeliveryChannel Channel, string? Recipient, DateTime SentAt, string? Note);

public record InvoiceSummaryResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public InvoiceProjectResponse Project { get; init; } = null!;

    /// <summary>The client the invoice was made for.</summary>
    public InvoiceClientResponse Client { get; init; } = null!;

    /// <summary>Empty until issued.</summary>
    public string? Number { get; init; }

    public InvoiceStatus Status { get; init; }

    public bool IsOverdue { get; init; }

    public string Currency { get; init; } = "USD";

    public decimal Total { get; init; }

    /// <summary>Live payments recorded against it, in the invoice currency.</summary>
    public decimal Paid { get; init; }

    public decimal Balance { get; init; }

    public DateTime? IssuedAt { get; init; }

    public DateOnly? DueOn { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed record InvoiceResponse : InvoiceSummaryResponse
{
    public InvoiceResponse()
    {
    }

    public InvoiceResponse(InvoiceSummaryResponse summary)
        : base(summary)
    {
    }

    public int TermsDays { get; init; }

    public string? Notes { get; init; }

    public string? BillToName { get; init; }

    public string? BillToContact { get; init; }

    public DateTime? VoidedAt { get; init; }

    public string? VoidReason { get; init; }

    public IReadOnlyList<InvoiceLineResponse> Lines { get; init; } = [];

    public IReadOnlyList<InvoiceDeliveryResponse> Deliveries { get; init; } = [];
}
