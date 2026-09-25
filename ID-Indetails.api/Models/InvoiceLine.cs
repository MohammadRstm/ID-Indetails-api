namespace ID_Indetails.api.Models;

public class InvoiceLine
{
    public long Id { get; set; }

    public long InvoiceId { get; set; }

    public int Position { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
