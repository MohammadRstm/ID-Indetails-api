namespace ID_Indetails.api.Business.Requirements;

/// <summary>
/// The requirements assigned to one person, on projects that are active or on hold:
/// <list type="bullet">
/// <item>Open ones are listed soonest deadline first; those without a deadline come last, oldest first.</item>
/// <item>Overdue: open with a deadline before today. Due soon: a deadline from today to a week ahead.</item>
/// <item>Done ones are only counted.</item>
/// </list>
/// </summary>
public static class MyRequirements
{
    public const int SoonDays = 7;

    public sealed record Item(long Id, long ProjectId, DateOnly? Deadline, bool IsDone, DateTime CreatedAt);

    public sealed record Summary(IReadOnlyList<Item> Open, int Projects, int Overdue, int DueSoon, int Done);

    public static Summary Summarise(IReadOnlyList<Item> items, DateOnly today)
    {
        var open = items
            .Where(i => !i.IsDone)
            .OrderBy(i => i.Deadline is null)
            .ThenBy(i => i.Deadline)
            .ThenBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .ToList();

        return new Summary(
            open,
            open.Select(i => i.ProjectId).Distinct().Count(),
            open.Count(i => RequirementRules.IsOverdue(i.Deadline, false, today)),
            open.Count(i => i.Deadline is { } d && d >= today && d <= today.AddDays(SoonDays)),
            items.Count(i => i.IsDone));
    }
}
