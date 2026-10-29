using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>One send of a BOQ to the client (PDF download, email or WhatsApp), with the total it had when it was sent.</summary>
public class BoqDelivery
{
    public long Id { get; set; }

    public long BoqId { get; set; }

    public DeliveryChannel Channel { get; set; }

    public string? Recipient { get; set; }

    public string? Note { get; set; }

    /// <summary>The BOQ's total when it was sent: a draft can still change afterwards.</summary>
    public decimal TotalUsd { get; set; }

    public DateTime SentAt { get; set; }

    public long? SentById { get; set; }

    public Boq Boq { get; set; } = null!;
}
