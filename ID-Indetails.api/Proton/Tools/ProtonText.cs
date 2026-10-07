using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>Small formatting rules shared by the tools, so every tool reads the same to the model.</summary>
public static class ProtonText
{
    /// <summary>Text people wrote, cut to <paramref name="max"/> characters so one long note can't flood the answer.</summary>
    public static string? Cut(string? text, int max)
        => text is null || text.Length <= max ? text : text[..max] + "…";

    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 1);

    public static decimal Hours(decimal hours) => Math.Round(hours, 1);

    /// <summary>An instant as the company's local date and time, which is how people talk about it.</summary>
    public static string Local(this IClock clock, DateTime utc) => clock.ToCompanyTime(utc).ToString("yyyy-MM-dd HH:mm");

    public static string? Local(this IClock clock, DateTime? utc) => utc is { } value ? clock.Local(value) : null;

    public static int Limit(int? requested, int fallback, int max) => Math.Clamp(requested ?? fallback, 1, max);
}

/// <summary>The two domains, as tool inputs: the model can only name one that exists.</summary>
public enum ProtonDomain
{
    Design,
    Execution,
}

public static class ProtonDomainCodes
{
    public static string Code(this ProtonDomain domain) => domain == ProtonDomain.Design ? Constants.DomainCodes.Design : Constants.DomainCodes.Execution;

    public static string? Code(this ProtonDomain? domain) => domain?.Code();
}
