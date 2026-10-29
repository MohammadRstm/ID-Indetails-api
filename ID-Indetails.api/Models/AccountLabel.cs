namespace ID_Indetails.api.Models;

/// <summary>
/// A company-wide label that payments and expenses are filed under ("Concrete", "Finishing", "Permits"). Super admins
/// edit the list in Settings. An archived label can't be picked any more but stays on what already carries it.
/// </summary>
public class AccountLabel
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Where it is in the list, first to last.</summary>
    public int Position { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
