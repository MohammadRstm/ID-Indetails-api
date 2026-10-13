using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Identity;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>ASP.NET Core Identity's password hasher (PBKDF2, salted, versioned), as for staff accounts.</summary>
public sealed class PortalPasswordService : IPortalPasswordService
{
    private readonly PasswordHasher<PortalAccount> _hasher = new();
    private readonly string _dummyHash;

    public PortalPasswordService()
    {
        _dummyHash = _hasher.HashPassword(new PortalAccount(), Guid.NewGuid().ToString("N"));
    }

    public string Hash(PortalAccount account, string password) => _hasher.HashPassword(account, password);

    public PasswordCheck Verify(PortalAccount account, string password)
    {
        if (string.IsNullOrEmpty(account.PasswordHash))
        {
            VerifyAgainstDummy(password);
            return PasswordCheck.Failed;
        }

        return _hasher.VerifyHashedPassword(account, account.PasswordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
    }

    public void VerifyAgainstDummy(string password)
        => _hasher.VerifyHashedPassword(new PortalAccount(), _dummyHash, password);
}
