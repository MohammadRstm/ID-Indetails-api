namespace ID_Indetails.api.Models;

/// <summary>
/// A category of company overhead ("Software &amp; services", "Rent &amp; utilities", …). Super admins add, rename and remove
/// them; one still used by a line can't be removed. Listed by <see cref="SortOrder"/>, "Other" last.
/// </summary>
public class OverheadGroup
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
