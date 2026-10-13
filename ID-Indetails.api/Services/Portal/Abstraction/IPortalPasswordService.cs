using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Auth.Abstraction;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>The same password hashing as staff accounts, for portal accounts.</summary>
public interface IPortalPasswordService
{
    string Hash(PortalAccount account, string password);

    PasswordCheck Verify(PortalAccount account, string password);

    /// <summary>Spends the time of a real check, so timing never tells which emails have accounts.</summary>
    void VerifyAgainstDummy(string password);
}
