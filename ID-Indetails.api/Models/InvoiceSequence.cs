namespace ID_Indetails.api.Models;

/// <summary>
/// The last invoice number used per domain per year. Locked while a number is issued so
/// two admins issuing at the same moment never get the same number.
/// </summary>
public class InvoiceSequence
{
    public string DomainCode { get; set; } = string.Empty;

    public int Year { get; set; }

    public int LastNumber { get; set; }

    public Domain Domain { get; set; } = null!;
}
