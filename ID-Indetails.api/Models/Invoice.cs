namespace ID_Indetails.api.Models;

/// <summary>
/// A client invoice. Status (draft, sent, partial, paid, overdue, void) is derived from
/// the dates and payments, never stored. Once issued an invoice is immutable: it is
/// corrected by voiding and reissuing.
/// </summary>
public class Invoice
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long ClientId { get; set; }

    /// <summary>PREFIX-YYYY-NNN, assigned when the invoice is issued. Null while a draft.</summary>
    public string? Number { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Sum of the lines, frozen once issued.</summary>
    public decimal Total { get; set; }

    public int TermsDays { get; set; }

    public string? Notes { get; set; }

    /// <summary>Snapshot of who was billed, taken at issue so later client edits don't change it.</summary>
    public string? BillToName { get; set; }

    public string? BillToContact { get; set; }

    public DateTime? IssuedAt { get; set; }

    public DateTime? DueAt { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? VoidReason { get; set; }

    public long? VoidedById { get; set; }

    /// <summary>
    /// A scheduled draft: the day the hourly job issues it and emails it to <see cref="SendTo"/>, from 9:00 (company
    /// time). Cleared once it is issued, however that happens.
    /// </summary>
    public DateOnly? SendOn { get; set; }

    /// <summary>The address a scheduled draft is emailed to; null when whoever scheduled it sends it themselves.</summary>
    public string? SendTo { get; set; }

    /// <summary>Who scheduled it: the email is signed with their name, and they are told when it goes out.</summary>
    public long? ScheduledById { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;

    public Client Client { get; set; } = null!;

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    public ICollection<InvoiceDelivery> Deliveries { get; set; } = new List<InvoiceDelivery>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
