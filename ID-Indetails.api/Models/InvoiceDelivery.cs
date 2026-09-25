using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>One send of an invoice. The first send issues the invoice; resends add rows.</summary>
public class InvoiceDelivery
{
    public long Id { get; set; }

    public long InvoiceId { get; set; }

    public DeliveryChannel Channel { get; set; }

    public string? Recipient { get; set; }

    public DateTime SentAt { get; set; }

    public long? SentById { get; set; }

    public string? Note { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
