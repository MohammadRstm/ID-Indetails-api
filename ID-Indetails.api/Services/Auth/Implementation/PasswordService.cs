using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.AspNetCore.Identity;

namespace ID_Indetails.api.Services.Auth.Implementation;

/// <summary>
/// ASP.NET Core Identity's password hasher (PBKDF2-HMAC-SHA512, salted, versioned), used
/// without the rest of Identity.
/// </summary>
public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _dummyHash;

    public PasswordService()
    {
        _dummyHash = _hasher.HashPassword(new User(), Guid.NewGuid().ToString("N"));
    }

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public PasswordCheck Verify(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            VerifyAgainstDummy(password);
            return PasswordCheck.Failed;
        }

        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
    }

    public void VerifyAgainstDummy(string password)
        => _hasher.VerifyHashedPassword(new User(), _dummyHash, password);
}
