namespace ID_Indetails.api.Business.Auth;

public static class EmailAddress
{
    /// <summary>
    /// The form every email is stored and looked up in, so "Rana@Example.com " and
    /// "rana@example.com" are the same account.
    /// </summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
