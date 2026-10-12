using System.Globalization;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>What the tools build their datasets from, so every dataset names its columns, periods and notes the same way.</summary>
public static class ProtonDatasets
{
    /// <summary>A table holds at most this many rows of a dataset; the rest are counted, not kept.</summary>
    public const int MaxRows = ProtonPresentationRules.MaxTableRows;

    public static ProtonColumn Label(string key, string label, ProtonColumnKind kind = ProtonColumnKind.Text) => new(key, label, kind);

    public static ProtonColumn Figure(string key, string label, ProtonUnit unit) => new(key, label, ProtonColumnKind.Number, unit);

    public static ProtonDataRow Row(ProtonRef? link, params ProtonCell[] cells) => new(cells, link);

    public static string Period(DayRange range) => Period(range.From, range.To);

    public static string Period(DateOnly from, DateOnly to) => from == to ? Day(from) : $"{Day(from)} – {Day(to)}";

    /// <summary>Only the datasets with rows: an empty one has nothing to show.</summary>
    public static IReadOnlyList<ProtonDataset> WithRows(params ProtonDataset?[] datasets)
        => datasets.OfType<ProtonDataset>().Where(d => d.Rows.Count > 0).ToList();

    /// <summary>
    /// Weekly figures of a period: each row is a whole week, the last one "so far" until it is over. The first weeks
    /// can start before the period (the report shows at least three), so the rows don't add up to its total.
    /// </summary>
    public static ProtonDataset Weeks(
        string title, IReadOnlyList<(DateOnly WeekStart, decimal Hours, int Entries)> weeks, DateOnly from, DateOnly to, DateOnly today)
    {
        var notes = new List<string>();
        if (weeks.Count > 0 && weeks[0].WeekStart < from)
        {
            notes.Add($"Each bar is a whole week, and the weeks before {Day(from)} are shown too: the bars don't add up to the period's total.");
        }

        if (weeks.Count > 0 && weeks[^1].WeekStart.AddDays(6) > to && to < today)
        {
            notes.Add($"The last week counts only the days up to {Day(to)}.");
        }

        return new ProtonDataset(
            title,
            [Label("week", "Week", ProtonColumnKind.Week), Figure("hours", "Hours", ProtonUnit.Hours), Figure("entries", "Entries", ProtonUnit.Count)],
            weeks.Select(w => new ProtonDataRow(
                [ProtonCell.Of(w.WeekStart), ProtonCell.Of(w.Hours), ProtonCell.Of(w.Entries)],
                Partial: w.WeekStart.AddDays(6) >= today && to >= today)).ToList(),
            Period(from, to),
            Notes: notes);
    }

    /// <summary>Monthly hours: one row a month, the current one "so far".</summary>
    public static ProtonDataset Months(string title, IReadOnlyList<(DateOnly Month, decimal Hours)> months, DateOnly today)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        return new ProtonDataset(
            title,
            [Label("month", "Month", ProtonColumnKind.Month), Figure("hours", "Hours", ProtonUnit.Hours)],
            months.Select(m => new ProtonDataRow([ProtonCell.Of(m.Month), ProtonCell.Of(m.Hours)], Partial: m.Month == thisMonth)).ToList(),
            months.Count == 0 ? null : Period(months[0].Month, today));
    }

    /// <summary>
    /// Daily minutes added up by month, for the months the days cover in full (a month whose first days weren't read
    /// would look smaller than it was), up to this one.
    /// </summary>
    public static IReadOnlyList<(DateOnly Month, decimal Hours)> ByMonth(IEnumerable<(DateOnly Day, int Minutes)> days, DateOnly readFrom, DateOnly today)
    {
        var first = readFrom.Day == 1 ? readFrom : new DateOnly(readFrom.Year, readFrom.Month, 1).AddMonths(1);
        var minutes = days.GroupBy(d => new DateOnly(d.Day.Year, d.Day.Month, 1)).ToDictionary(g => g.Key, g => g.Sum(d => d.Minutes));
        var months = new List<(DateOnly, decimal)>();
        for (var month = first; month <= today; month = month.AddMonths(1))
        {
            months.Add((month, ProtonText.Hours(minutes.GetValueOrDefault(month))));
        }

        return months;
    }

    /// <summary>
    /// Headline figures of a period and, when compared, the one before: the first row is the period, the second the
    /// one it is compared with.
    /// </summary>
    public static ProtonDataset Compared(
        string title, ProtonPeriodPair pair, IReadOnlyList<ProtonColumn> figures, IReadOnlyList<decimal?> now, IReadOnlyList<decimal?>? before, IReadOnlyList<string>? notes = null)
    {
        var rows = new List<ProtonDataRow> { new([ProtonCell.Of(Period(pair.Current)), .. now.Select(ProtonCell.Of)], Partial: pair.Partial) };
        if (pair.Previous is { } previous && before is not null)
        {
            rows.Add(new([ProtonCell.Of(Period(previous)), .. before.Select(ProtonCell.Of)]));
        }

        var said = new List<string>();
        if (pair.Note is { } note)
        {
            said.Add(note);
        }

        said.AddRange(notes ?? []);
        return new ProtonDataset(title, [Label("period", "Period"), .. figures], rows, Period(pair.Current), Compared: rows.Count == 2, Notes: said);
    }

    public static ProtonCell Status(ProjectStatus status) => ProtonCell.Of(status switch
    {
        ProjectStatus.Active => "Active",
        ProjectStatus.Hold => "On hold",
        ProjectStatus.Completed => "Completed",
        _ => "Archived",
    });

    private static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
