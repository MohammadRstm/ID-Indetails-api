namespace ID_Indetails.api.Proton.Agent;

/// <summary>
/// What a live interface hears while Proton answers: what it is doing, in a fixed phrase per tool, and the answer as it
/// is written. The JSON endpoint has no listener and gets the whole reply at the end.
/// </summary>
public interface IProtonListener
{
    Task StatusAsync(string phrase, CancellationToken cancellationToken);

    /// <param name="trip">Which model call wrote it: text from an earlier call is replaced by a later one's.</param>
    Task DeltaAsync(int trip, string text, CancellationToken cancellationToken);
}

/// <summary>
/// The status line for a tool: always one of these fixed phrases, never the tool's input or anything it read, so a
/// status can't carry data the answer wouldn't.
/// </summary>
public static class ProtonStatus
{
    private static readonly Dictionary<string, string> Phrases = new(StringComparer.Ordinal)
    {
        ["find"] = "Looking it up…",
        ["list_projects"] = "Going through the projects…",
        ["get_project"] = "Looking at the project…",
        ["review_project"] = "Reviewing the project…",
        ["get_project_requirements"] = "Checking the requirements…",
        ["get_project_team"] = "Checking who worked on it…",
        ["get_attention"] = "Checking what needs attention…",
        ["get_company_summary"] = "Putting a summary together…",
        ["summarize_changes"] = "Looking at what changed…",
        ["compare_work"] = "Comparing the hours…",
        ["compare_money"] = "Comparing the figures…",
        ["open_record"] = "Opening it…",
        ["open_page"] = "Opening it…",
        ["offer_choices"] = "Lining up the options…",
        ["suggest_replies"] = "Thinking of next steps…",
        ["refresh_page"] = "Refreshing the page…",
        ["present_data"] = "Laying out the figures…",
    };

    public const string Default = "Looking into it…";

    public static string Of(string tool)
        => Phrases.TryGetValue(tool, out var phrase) ? phrase
            : tool.StartsWith("propose_", StringComparison.Ordinal) ? "Preparing the change…"
            : Default;
}
