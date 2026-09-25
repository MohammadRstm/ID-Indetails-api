using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

public class ClientContact
{
    public long Id { get; set; }

    public long ClientId { get; set; }

    public ContactKind Kind { get; set; }

    /// <summary>As entered.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Lower-cased email or digits-only phone, used to detect duplicate contacts.</summary>
    public string NormalizedValue { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsWhatsapp { get; set; }

    public Client Client { get; set; } = null!;
}
