namespace ID_Indetails.api.Models;

/// <summary>
/// One material on a statement, with what the client was shown at that moment. A material
/// that appears on any statement is "stated": its client price is frozen from then on.
/// Type rows and totals on the statement are calculated from these lines, not stored.
/// </summary>
public class ClientStatementLine
{
    public long Id { get; set; }

    public long StatementId { get; set; }

    public long MaterialId { get; set; }

    public int Position { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    public string? Unit { get; set; }

    /// <summary>Client price shown on the statement.</summary>
    public decimal Price { get; set; }

    /// <summary>True when the material was already on an earlier statement.</summary>
    public bool IsEarlier { get; set; }

    /// <summary>False when the row was collapsed into an "earlier materials" line.</summary>
    public bool IsListed { get; set; } = true;

    public ClientStatement Statement { get; set; } = null!;

    public ProjectMaterial Material { get; set; } = null!;
}
