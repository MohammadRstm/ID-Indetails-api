namespace ID_Indetails.api.Models;

/// <summary>
/// One change of a client project's agreed payment, with why it changed: a Design project's customer payment, or an
/// Execution site's project fee and material markup. The first is the payment agreed at the start. Never edited.
/// </summary>
public class ProjectPaymentChange
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>The payment after the change, in USD: Design's customer payment, Execution's project fee.</summary>
    public decimal Amount { get; set; }

    /// <summary>Null for the payment agreed at the start.</summary>
    public decimal? PreviousAmount { get; set; }

    /// <summary>Execution only: the default material markup after the change, a fraction (0.125 is 12.5%).</summary>
    public decimal? MaterialMarkup { get; set; }

    public decimal? PreviousMaterialMarkup { get; set; }

    /// <summary>The project's TVA rate after the change; null on rows from before TVA.</summary>
    public decimal? TvaRate { get; set; }

    public decimal? PreviousTvaRate { get; set; }

    /// <summary>Execution only: the company's supervision rate after the change; null on Design rows and on rows from before supervision.</summary>
    public decimal? SupervisionRate { get; set; }

    public decimal? PreviousSupervisionRate { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    public long? ChangedById { get; set; }

    public Project Project { get; set; } = null!;
}
