namespace ID_Indetails.api.Models;

/// <summary>
/// Last statement number used per calendar year. Locked while a number is issued so two
/// admins never get the same number.
/// </summary>
public class StatementSequence
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}
