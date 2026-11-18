namespace ID_Indetails.api.DTO.Request.DevSignIn;

/// <summary>Development only: the staff member to sign in as.</summary>
public sealed record DevSignInRequest
{
    public long UserId { get; init; }
}

/// <summary>Development only: the client's portal account to sign in as.</summary>
public sealed record DevPortalSignInRequest
{
    public long AccountId { get; init; }
}
