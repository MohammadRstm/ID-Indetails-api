namespace ID_Indetails.api.Models;

/// <summary>Invoice settings for one domain (HL for Design, EX for Execution).</summary>
public class BillingSettings
{
    public string DomainCode { get; set; } = string.Empty;

    public string InvoicePrefix { get; set; } = string.Empty;

    public int DefaultTermsDays { get; set; } = 30;

    public string? BankAccount { get; set; }

    public string? BankName { get; set; }

    public string? Iban { get; set; }

    public string? Swift { get; set; }

    public string? TermsText { get; set; }

    public string? Footer { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedById { get; set; }

    public Domain Domain { get; set; } = null!;
}
