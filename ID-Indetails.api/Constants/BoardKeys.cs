namespace ID_Indetails.api.Constants;

/// <summary>The dashboards whose cards people can rearrange (see <c>/api/me/board-layouts</c>).</summary>
public static class BoardKeys
{
    public const string DesignOverview = "overview-design";
    public const string ExecutionOverview = "overview-execution";
    public const string CompanyOverview = "company-overview";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { DesignOverview, ExecutionOverview, CompanyOverview };
}
