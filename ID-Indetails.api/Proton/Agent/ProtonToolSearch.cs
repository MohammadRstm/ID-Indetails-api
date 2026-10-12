namespace ID_Indetails.api.Proton.Agent;

/// <summary>
/// Past <c>Proton:DeferToolsAbove</c> tools, only these are sent in full; the rest are deferred and found through the
/// provider's tool search when a question needs them. The set is what most questions start with: finding a record,
/// the overviews and the analyses behind most "how is it going" questions, moving around the app and offering options.
/// </summary>
public static class ProtonToolSearch
{
    public static readonly IReadOnlySet<string> Core = new HashSet<string>(StringComparer.Ordinal)
    {
        "find", "get_attention", "list_projects", "get_project", "list_clients", "get_client", "get_person",
        "get_domain_dashboard", "get_recent_activity",
        "compare_work", "compare_money", "summarize_changes", "review_project", "get_project_financials", "get_time_summary",
        "get_person_work", "list_work_entries",
        "open_record", "open_page", "open_surface", "offer_choices", "suggest_replies", "present_data", "refresh_page",
    };
}
