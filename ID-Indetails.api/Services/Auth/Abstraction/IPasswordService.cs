using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Auth.Abstraction;

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Correct, but hashed with older settings: store a fresh hash.</summary>
    SuccessRehashNeeded,
}

public interface IPasswordService
{
    string Hash(User user, string password);

    PasswordCheck Verify(User user, string password);

    /// <summary>
    /// Spends the same time as a real verification. Used when the email is unknown, so response
    /// timing does not reveal which emails have accounts.
    /// </summary>
    void VerifyAgainstDummy(string password);
}
