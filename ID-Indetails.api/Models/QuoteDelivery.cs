using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>One send of a quote (PDF download, email or WhatsApp).</summary>
public class QuoteDelivery
{
    public long Id { get; set; }

    public long QuoteId { get; set; }

    public DeliveryChannel Channel { get; set; }

    public string? Recipient { get; set; }

    public DateTime SentAt { get; set; }

    public long? SentById { get; set; }

    public string? Note { get; set; }

    public Quote Quote { get; set; } = null!;
}

/// <summary>Last quote number used per calendar year, bumped atomically so two quotes never share a number.</summary>
public class QuoteSequence
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}
