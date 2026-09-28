using System.Security.Cryptography;

namespace ID_Indetails.api.Services.Common.Implementation;

/// <summary>Generates one-time passwords (e.g. for the first super admin).</summary>
public static class PasswordGenerator
{
    // Look-alike characters (0/O, 1/l/I) are left out so the password is easy to type from an email.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string All = Upper + Lower + Digits + Symbols;

    /// <summary>A random password containing at least one of each character class.</summary>
    public static string Generate(int length = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 12);

        var chars = new char[length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < length; i++)
        {
            chars[i] = Pick(All);
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
