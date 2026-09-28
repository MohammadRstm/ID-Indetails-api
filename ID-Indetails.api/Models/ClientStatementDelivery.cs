using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>One send of a statement (PDF download, email or WhatsApp).</summary>
public class ClientStatementDelivery
{
    public long Id { get; set; }

    public long StatementId { get; set; }

    public DeliveryChannel Channel { get; set; }

    public string? Recipient { get; set; }

    public DateTime SentAt { get; set; }

    public long? SentById { get; set; }

    public string? Note { get; set; }

    public ClientStatement Statement { get; set; } = null!;
}
