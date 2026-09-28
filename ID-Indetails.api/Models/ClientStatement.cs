using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// Execution only: a client-facing statement of materials supplied, with a payment summary.
/// It is not an invoice and changes nothing that is owed. Once created it is an immutable
/// snapshot of that day's figures: no edit, void or delete.
/// </summary>
public class ClientStatement
{
    public long Id { get; set; }

    /// <summary>ST-YYYY-NNN, one sequence per calendar year.</summary>
    public string Number { get; set; } = string.Empty;

    public long ProjectId { get; set; }

    public long ClientId { get; set; }

    public StatementMode Mode { get; set; }

    /// <summary>Whether materials from earlier statements are shown again.</summary>
    public bool IncludesEarlier { get; set; }

    /// <summary>Date of the previous statement, for "supplied since …". Null on the first one.</summary>
    public DateOnly? Since { get; set; }

    /// <summary>Snapshot: payments received (USD) when the statement was made.</summary>
    public decimal ReceivedUsd { get; set; }

    /// <summary>Snapshot: agreed amount (material client prices + project fee) when made.</summary>
    public decimal AgreedUsd { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public Project Project { get; set; } = null!;

    public Client Client { get; set; } = null!;

    public ICollection<ClientStatementLine> Lines { get; set; } = new List<ClientStatementLine>();

    public ICollection<ClientStatementDelivery> Deliveries { get; set; } = new List<ClientStatementDelivery>();
}
