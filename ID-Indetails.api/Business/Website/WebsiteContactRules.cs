using System.Text.RegularExpressions;

namespace ID_Indetails.api.Business.Website;

/// <summary>The website's contact form: the same limits its page checks.</summary>
public static partial class WebsiteContactRules
{
    public const int MaxNameLength = 120;
    public const int MinMessageLength = 10;
    public const int MaxMessageLength = 4000;
    public const int MaxPhoneLength = 50;

    /// <summary>Digits, spaces, ( ) . - and an optional leading +, with at least 6 digits.</summary>
    public static bool IsPhone(string value)
        => value.Length <= MaxPhoneLength && Phone().IsMatch(value) && value.Count(char.IsAsciiDigit) >= 6;

    /// <summary>Anything in the hidden field means a robot filled the form: the message is dropped without a word.</summary>
    public static bool IsRobot(string? honeypot) => !string.IsNullOrEmpty(honeypot);

    [GeneratedRegex(@"^\+?[0-9 ().\-]+$")]
    private static partial Regex Phone();
}
