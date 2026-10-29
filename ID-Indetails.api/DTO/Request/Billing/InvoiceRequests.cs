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

    /// <summary>Only invoices issued on or after this day (company time). Drafts have no issue day, so a date leaves them out.</summary>
    public DateOnly? IssuedFrom { get; init; }

    /// <summary>Only invoices issued on or before this day (company time).</summary>
    public DateOnly? IssuedTo { get; init; }

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

/// <summary>Auto invoice: what is left to bill, split into drafts that are issued and emailed on their own days.</summary>
public sealed record InvoicePlanRequest
{
    public IReadOnlyList<PlannedInvoiceRequest> Invoices { get; init; } = [];

    /// <summary>Defaults to the domain's billing settings.</summary>
    public int? TermsDays { get; init; }

    /// <summary>Printed on each invoice for the client.</summary>
    public string? Notes { get; init; }

    /// <summary>The client address each is emailed to on its day; null to be told to send them yourself.</summary>
    public string? SendTo { get; init; }
}

public sealed record PlannedInvoiceRequest
{
    /// <summary>Today or later (company time zone).</summary>
    public DateOnly SendOn { get; init; }

    public decimal Amount { get; init; }

    /// <summary>Its one line, e.g. "Villa Mounir - payment 2 of 3".</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>When a draft goes out, and where; see <see cref="InvoicePlanRequest"/>.</summary>
public sealed record ScheduleInvoiceRequest
{
    public DateOnly SendOn { get; init; }

    public string? SendTo { get; init; }
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

/// <summary>Emails an invoice or quote to the client with its PDF attached.</summary>
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
