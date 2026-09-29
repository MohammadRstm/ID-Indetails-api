namespace ID_Indetails.api.DTO.Request.Auth;

public sealed record ForgotPasswordRequest
{
    public string Email { get; init; } = string.Empty;
}
