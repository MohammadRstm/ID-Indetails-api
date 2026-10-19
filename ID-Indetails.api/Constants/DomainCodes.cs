namespace ID_Indetails.api.Constants;

/// <summary>
/// The domains a person can belong to. They are not tenants: this is a single-company system, and a domain only decides
/// which division's data a user may see. Super admins belong to no domain and see them all.
/// <list type="bullet">
/// <item>The two businesses, design and execution, have projects, clients and money.</item>
/// <item>Website is a service: its staff (admins only) manage the company's public website and nothing else.</item>
/// </list>
/// </summary>
public static class DomainCodes
{
    public const string Design = "design";
    public const string Execution = "execution";
    public const string Website = "website";

    /// <summary>Every domain, in the order they are listed.</summary>
    public static readonly IReadOnlyList<string> All = [Design, Execution, Website];

    /// <summary>The domains with projects, clients and money.</summary>
    public static readonly IReadOnlyList<string> Businesses = [Design, Execution];

    public static bool IsBusiness(string? domain) => domain is Design or Execution;
}
