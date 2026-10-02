using System.Text.Json.Serialization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Billing;

public sealed record InvoiceProjectResponse(long Id, string Title, string? Location = null);

public sealed record InvoiceClientResponse(long Id, string DisplayName);

public sealed record InvoiceLineResponse(long Id, int Position, string Description, decimal Amount);

/// <param name="IsReminder">An automatic reminder of the overdue invoice.</param>
public sealed record InvoiceDeliveryResponse(long Id, DeliveryChannel Channel, string? Recipient, DateTime SentAt, string? Note, bool IsReminder = false);

public record InvoiceSummaryResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public InvoiceProjectResponse Project { get; init; } = null!;

    /// <summary>The client the invoice was made for.</summary>
    public InvoiceClientResponse Client { get; init; } = null!;

    /// <summary>Empty until issued.</summary>
    public string? Number { get; init; }

    /// <summary>Partial and paid show as sent to callers without financial:view.</summary>
    public InvoiceStatus Status { get; init; }

    /// <summary>Left out for callers without financial:view.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsOverdue { get; init; }

    public string Currency { get; init; } = "USD";

    public decimal Total { get; init; }

    /// <summary>Live payments recorded against it, in USD. Left out for callers without financial:view.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Paid { get; init; }

    /// <summary>Left out for callers without financial:view.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Balance { get; init; }

    public DateTime? IssuedAt { get; init; }

    public DateOnly? DueOn { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>A scheduled draft's day to go out; null for anything else.</summary>
    public DateOnly? SendOn { get; init; }
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

    /// <summary>Who drafted it; null when that isn't known.</summary>
    public PersonRefResponse? CreatedBy { get; init; }

    /// <summary>Where a scheduled draft is emailed on its day; null when whoever scheduled it sends it themselves.</summary>
    public string? SendTo { get; init; }

    /// <summary>Who scheduled it, while it is scheduled.</summary>
    public PersonRefResponse? ScheduledBy { get; init; }

    public IReadOnlyList<InvoiceLineResponse> Lines { get; init; } = [];

    public IReadOnlyList<InvoiceDeliveryResponse> Deliveries { get; init; } = [];
}
