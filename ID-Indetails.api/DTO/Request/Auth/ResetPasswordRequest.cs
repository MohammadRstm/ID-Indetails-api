namespace ID_Indetails.api.DTO.Request.Auth;

/// <summary>The token from the emailed link, sent in the body to keep it out of access logs, and the new password.</summary>
public sealed record ResetPasswordRequest
{
    public string Token { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
