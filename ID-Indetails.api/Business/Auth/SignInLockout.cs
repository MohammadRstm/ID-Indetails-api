namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Staff sign-in pauses for an account after repeated wrong passwords, whatever IP they come from. While paused, even
/// the right password gets the same answer as an unknown email.
/// </summary>
public static class SignInLockout
{
    public const int Attempts = 5;

    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(10);

    public static bool IsLocked(DateTime? lockedUntil, DateTime now) => lockedUntil > now;
}
