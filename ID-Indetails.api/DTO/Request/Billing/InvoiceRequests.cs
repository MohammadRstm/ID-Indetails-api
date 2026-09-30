using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Billing;

public sealed record InvoiceListQuery
{
    public long? ProjectId { get; init; }

    public long? ClientId { get; init; }

    public InvoiceStatus? Status { get; init; }

    /// <summary>Only sent or part-paid invoices past their due date.</summary>
    public bool? Overdue { get; init; }

    /// <summary>Must be a domain the caller can see, or nothing is returned.</summary>
    public string? Domain { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record InvoiceLineRequest
{
    public string Description { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

/// <summary>Creates a draft, or replaces a draft's lines, terms, notes and bill-to details.</summary>
public sealed record SaveInvoiceRequest
{
    public IReadOnlyList<InvoiceLineRequest> Lines { get; init; } = [];

    /// <summary>Defaults to the domain's billing settings (30 days before they exist).</summary>
    public int? TermsDays { get; init; }

    /// <summary>Printed on the invoice for the client.</summary>
    public string? Notes { get; init; }

    public string? BillToName { get; init; }

    public string? BillToContact { get; init; }
}

public sealed record IssueInvoiceRequest
{
    /// <summary>Today or an earlier day (company time zone). Defaults to today.</summary>
    public DateOnly? IssuedOn { get; init; }
}

public sealed record RecordDeliveryRequest
{
    public DeliveryChannel Channel { get; init; }

    public string? Recipient { get; init; }

    public string? Note { get; init; }
}

/// <summary>Emails an invoice or statement to the client with its PDF attached.</summary>
public sealed record EmailDocumentRequest
{
    public string To { get; init; } = string.Empty;

    /// <summary>Kept with the delivery; not in the email.</summary>
    public string? Note { get; init; }
}

public sealed record VoidRequest
{
    public string Reason { get; init; } = string.Empty;
}
