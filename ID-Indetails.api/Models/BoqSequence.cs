namespace ID_Indetails.api.Models;

/// <summary>
/// The last BOQ number used per prefix (BQ or VO) per calendar year. Locked while a number is issued so two
/// admins never get the same number.
/// </summary>
public class BoqSequence
{
    public string Prefix { get; set; } = string.Empty;

    public int Year { get; set; }

    public int LastNumber { get; set; }
}
