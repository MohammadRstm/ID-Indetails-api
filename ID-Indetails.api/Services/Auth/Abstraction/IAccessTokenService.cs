using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Auth.Abstraction;

public interface IAccessTokenService
{
    /// <summary>A signed JWT for the user. <paramref name="user"/> must have its Team loaded.</summary>
    (string Token, DateTime ExpiresAt) Create(User user);
}
