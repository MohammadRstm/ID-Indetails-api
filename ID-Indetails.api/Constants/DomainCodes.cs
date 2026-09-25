namespace ID_Indetails.api.Constants;

/// <summary>
/// The company's two business divisions. They are not tenants: this is a single-company
/// system, and a domain only decides which division's data a user may see.
/// Super admins belong to no domain and see both.
/// </summary>
public static class DomainCodes
{
    public const string Design = "design";
    public const string Execution = "execution";

    public static readonly IReadOnlyList<string> All = [Design, Execution];
}
