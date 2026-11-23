namespace ID_Indetails.api.Models;

/// <summary>
/// What a project's cost was for, per business: Design project expenses and Execution site expenses each have their own
/// list. Expenses keep the <see cref="Code"/>, which never changes; the <see cref="Name"/> can.
/// </summary>
public class ExpenseCategory
{
    public long Id { get; set; }

    public string DomainCode { get; set; } = string.Empty;

    /// <summary>What expenses store: lowercase, made from the first name, fixed once made.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Where it shows in the business's list.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public Domain Domain { get; set; } = null!;
}
