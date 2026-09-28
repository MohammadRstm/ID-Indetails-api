namespace ID_Indetails.api.Business.Common;

public static class PersonName
{
    /// <summary>First and last name, or the email while someone has no name yet (an invited account).</summary>
    public static string Of(string? firstName, string? lastName, string email)
        => $"{firstName} {lastName}".Trim() is { Length: > 0 } name ? name : email;
}
