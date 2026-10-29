namespace ID_Indetails.api.Models;

/// <summary>
/// A quote sent to a client: what the whole project costs them, TVA included (never shown apart). A snapshot of the
/// project's payment and TVA when it was made; a change of either makes a new quote. Never edited.
/// </summary>
public class Quote
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long ClientId { get; set; }

    /// <summary>QU-YYYY-NNN, numbered per calendar year.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The agreed payment (Design) or agreed amount before materials (Execution), before TVA. Internal only.</summary>
    public decimal NetUsd { get; set; }

    /// <summary>The project's TVA rate when quoted. Internal only.</summary>
    public decimal TvaRate { get; set; }

    /// <summary>What the client is quoted: the net amount with TVA in it.</summary>
    public decimal TotalUsd { get; set; }

    /// <summary>The project's description when quoted: the scope the client reads.</summary>
    public string? Scope { get; set; }

    public DateTime CreatedAt { get; set; }

    public long CreatedById { get; set; }

    public Project Project { get; set; } = null!;

    public Client Client { get; set; } = null!;

    public ICollection<QuoteDelivery> Deliveries { get; set; } = new List<QuoteDelivery>();
}
