using System.Security.Cryptography;
using System.Text;
using ID_Indetails.api.Business.Auth;

namespace ID_Indetails.api.Business.Proton;

/// <summary>What a saved chat is called, and whether its owner may still read it.</summary>
public static class ProtonHistoryRules
{
    public const int TitleLength = 60;

    /// <summary>A new chat's name: its first question on one line, shortened at a word where it can be.</summary>
    public static string TitleOf(string question)
    {
        var line = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0)
        {
            return "New chat";
        }

        if (line.Length <= TitleLength)
        {
            return line;
        }

        var cut = line[..TitleLength];
        var space = cut.LastIndexOf(' ');
        return (space > TitleLength / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }

    /// <summary>The access a chat was held under, as stored with it: a hash of its fingerprint, compared the same way.</summary>
    public static string AccessKey(string accessFingerprint)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessFingerprint)));

    /// <summary>
    /// Whether the owner has lost any of the access they had when the chat was held: then its answers could show what
    /// they may no longer see, so it isn't shown. Gaining access changes nothing.
    /// </summary>
    public static bool LostAccess(IEnumerable<string> permissionsThen, IEnumerable<string> domainsThen, UserAccess now)
        => permissionsThen.Any(p => !now.Has(p)) || domainsThen.Any(d => !now.CanSee(d));
}
