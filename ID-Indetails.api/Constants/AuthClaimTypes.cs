namespace ID_Indetails.api.Constants;

/// <summary>Claim names in the access token. Kept short and stable: the client reads them.</summary>
public static class AuthClaimTypes
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string FirstName = "given_name";
    public const string LastName = "family_name";
    public const string Role = "role";

    /// <summary>design / execution. Absent for super admins.</summary>
    public const string Domain = "domain";

    /// <summary>Team (department) name. Absent when the user has no team.</summary>
    public const string Team = "team";

    public const string Position = "position";

    /// <summary>"true" until a generated password has been replaced.</summary>
    public const string MustChangePassword = "must_change_password";

    /// <summary>The phone app's session id. Only on tokens issued to the app; device registration ties a push token to it.</summary>
    public const string SessionId = "sid";

    /// <summary>
    /// The client portal account id. Portal tokens carry it instead of <see cref="UserId"/>, so nothing that reads a
    /// staff user id ever finds one in a client's token.
    /// </summary>
    public const string PortalAccountId = "portal_account";
}
