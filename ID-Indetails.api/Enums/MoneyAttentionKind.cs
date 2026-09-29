namespace ID_Indetails.api.Enums;

/// <summary>A money follow-up of a domain dashboard (see <c>Business/Finance/MoneyAttention</c>).</summary>
public enum MoneyAttentionKind
{
    OverdueInvoice,

    /// <summary>A completed project still owed on its invoices, with nothing overdue.</summary>
    FinishedOwed,

    /// <summary>An active client project with no payment agreed.</summary>
    NoPayment,

    /// <summary>An active client project whose cost has passed 85% of its payment.</summary>
    CostHigh,
}
