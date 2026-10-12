namespace ID_Indetails.api.Proton.Ui;

public enum ProtonPresentSort
{
    ValueDesc,
    ValueAsc,
    Label,
}

/// <summary>A dataset shown as asked, or why it can't be, with the forms that would fit instead.</summary>
public sealed record ProtonPresentation(ProtonBlock? Block, string? Refusal);

/// <summary>
/// Whether a dataset can honestly be shown in a form, and the block when it can: a trend needs points in time, a
/// composition needs every part of a whole, a chart never mixes units, a missing value is never drawn as 0, and what
/// was left out is said under the block. The model proposes; these rules decide.
/// </summary>
public static class ProtonPresentationRules
{
    public const int MaxBarPoints = 30;
    public const int MaxTimePoints = 60;
    public const int MinTimePoints = 3;
    public const int MaxCompositionParts = 8;
    public const int MaxKpis = 6;
    public const int MaxTableRows = 100;
    public const int MaxTableColumns = 8;
    public const int MaxSeries = 3;
    public const int MaxTitle = 80;

    /// <summary>The forms a dataset fits as it is, for its descriptor: what the model may choose from.</summary>
    public static IReadOnlyList<ProtonPresentForm> FormsFor(ProtonDataset dataset)
        => Enum.GetValues<ProtonPresentForm>()
            .Where(form => Present(new ProtonHeldDataset("d", "tool", dataset, DateTime.UnixEpoch), form, null, null, null, null, "b").Block is not null)
            .ToList();

    public static ProtonPresentation Present(
        ProtonHeldDataset held,
        ProtonPresentForm form,
        IReadOnlyList<string>? columns,
        ProtonPresentSort? sort,
        int? top,
        string? title,
        string blockId)
    {
        var data = held.Dataset;
        if (data.Columns.Count < 2 || data.Rows.Count == 0)
        {
            return Refuse("This dataset has nothing to show: say so in words.");
        }

        var chosen = Choose(data, form, columns, out var problem);
        if (problem is not null)
        {
            return Refuse(problem);
        }

        var notes = new List<string>(data.Notes ?? []);
        var source = new ProtonBlockSource(held.Tool, held.Id, held.AsOf);
        var name = TitleOf(title, data.Title);
        return form switch
        {
            ProtonPresentForm.Bar => Bar(data, chosen, sort, top, notes, blockId, name, source),
            ProtonPresentForm.Columns or ProtonPresentForm.Line => OverTime(data, form, chosen, top, notes, blockId, name, source),
            ProtonPresentForm.Composition => Composition(data, chosen, notes, blockId, name, source),
            ProtonPresentForm.Kpis => Kpis(data, chosen, notes, blockId, name, source),
            _ => Table(data, chosen, sort, top, notes, blockId, name, source),
        };
    }

    /// <summary>The columns to show: those asked for (numbers only in a chart), or the form's default.</summary>
    private static IReadOnlyList<int> Choose(ProtonDataset data, ProtonPresentForm form, IReadOnlyList<string>? keys, out string? problem)
    {
        problem = null;
        var numbers = Enumerable.Range(1, data.Columns.Count - 1).Where(i => data.Columns[i].IsNumber).ToList();
        if (keys is { Count: > 0 })
        {
            var picked = new List<int>();
            foreach (var key in keys.Distinct())
            {
                var at = data.Columns.ToList().FindIndex(c => c.Key == KeyOf(key));
                if (at <= 0 || (form != ProtonPresentForm.Table && !data.Columns[at].IsNumber))
                {
                    problem = $"There is no {(form == ProtonPresentForm.Table ? "" : "number ")}column \"{key}\". "
                        + $"Its {(form == ProtonPresentForm.Table ? "" : "number ")}columns are: "
                        + string.Join(", ", (form == ProtonPresentForm.Table ? Enumerable.Range(1, data.Columns.Count - 1) : numbers).Select(i => data.Columns[i].Key)) + ".";
                    return [];
                }

                picked.Add(at);
            }

            return picked;
        }

        if (form == ProtonPresentForm.Table)
        {
            return Enumerable.Range(1, data.Columns.Count - 1).ToList();
        }

        if (numbers.Count == 0)
        {
            problem = "This dataset has no figures to chart: show it as a table.";
            return [];
        }

        return form == ProtonPresentForm.Kpis ? numbers : [numbers[0]];
    }

    private static ProtonPresentation Bar(
        ProtonDataset data, IReadOnlyList<int> chosen, ProtonPresentSort? sort, int? top, List<string> notes, string id, string title, ProtonBlockSource source)
    {
        var label = data.Columns[0];
        if (label.IsTime)
        {
            return Refuse("These rows are points in time: use as=columns or as=line.");
        }

        if (data.Rows.Count < 2)
        {
            return Refuse($"A comparison needs at least 2 rows; this has {data.Rows.Count}. Use as=kpis, or say it in words.");
        }

        if (Series(data, chosen, out var series) is { } mixed)
        {
            return Refuse(mixed);
        }

        var rows = Sorted(data, chosen[0], sort ?? ProtonPresentSort.ValueDesc).ToList();
        var shown = Math.Min(Math.Clamp(top ?? MaxBarPoints, 1, MaxBarPoints), rows.Count);
        Shown(notes, shown, data.Total, label.Label);
        var points = rows.Take(shown).Select(r => Point(r, chosen)).ToList();
        return Done(new ProtonBlock(id, ProtonBlockType.Chart, title, data.Period,
            new ProtonChartBlock(ProtonPresentForm.Bar, label.Label, label.Kind, series, points), null, null, notes, source));
    }

    private static ProtonPresentation OverTime(
        ProtonDataset data, ProtonPresentForm form, IReadOnlyList<int> chosen, int? top, List<string> notes, string id, string title, ProtonBlockSource source)
    {
        var label = data.Columns[0];
        var kind = form == ProtonPresentForm.Line ? "line" : "chart over time";
        if (!label.IsTime)
        {
            return Refuse($"A {kind} needs points in time; these rows are {label.Label.ToLowerInvariant()}. Use as=bar or as=table.");
        }

        if (data.Rows.Count < MinTimePoints)
        {
            return Refuse($"A {kind} needs at least {MinTimePoints} points in time; this has {data.Rows.Count}. Use as=kpis or as=table.");
        }

        if (Series(data, chosen, out var series) is { } mixed)
        {
            return Refuse(mixed);
        }

        // in time order; a long series keeps its latest points
        var rows = data.Rows.OrderBy(r => r.Cells[0].Date).ToList();
        var shown = Math.Min(Math.Clamp(top ?? MaxTimePoints, MinTimePoints, MaxTimePoints), rows.Count);
        if (shown < rows.Count)
        {
            notes.Add($"Shows the latest {shown} of {rows.Count} {Plural(label.Label)}.");
        }

        if (rows.Any(r => r.Partial))
        {
            notes.Add($"The last {label.Label.ToLowerInvariant()} isn't over: its figures are so far.");
        }

        var points = rows.Skip(rows.Count - shown).Select(r => Point(r, chosen)).ToList();
        return Done(new ProtonBlock(id, ProtonBlockType.Chart, title, data.Period,
            new ProtonChartBlock(form, label.Label, label.Kind, series, points), null, null, notes, source));
    }

    private static ProtonPresentation Composition(
        ProtonDataset data, IReadOnlyList<int> chosen, List<string> notes, string id, string title, ProtonBlockSource source)
    {
        if (chosen.Count != 1)
        {
            return Refuse("A composition shows one figure split into its parts: pick one column.");
        }

        if (!data.PartsOfWhole)
        {
            return Refuse("These rows aren't the parts of one whole, so a composition would mislead. Use as=bar.");
        }

        if (!data.Complete)
        {
            return Refuse($"Only {data.Rows.Count} of {data.Total} parts are here, so the whole can't be shown. Use as=bar or as=table.");
        }

        var values = data.Rows.Select(r => r.Cells[chosen[0]].Number).ToList();
        if (values.Any(v => v is null or < 0))
        {
            return Refuse("Some parts are missing or below zero, so they can't be shown as shares of a whole. Use as=bar or as=table.");
        }

        if (data.Rows.Count is < 2 or > MaxCompositionParts)
        {
            return Refuse($"A composition fits 2 to {MaxCompositionParts} parts; this has {data.Rows.Count}. Use as=bar.");
        }

        if (values.Sum() == 0)
        {
            return Refuse("Every part is 0: say so in words.");
        }

        var column = data.Columns[chosen[0]];
        var series = new[] { new ProtonSeries(column.Key, column.Label, column.Unit ?? ProtonUnit.Count) };
        var points = data.Rows.Select(r => Point(r, chosen)).ToList();
        return Done(new ProtonBlock(id, ProtonBlockType.Chart, title, data.Period,
            new ProtonChartBlock(ProtonPresentForm.Composition, data.Columns[0].Label, data.Columns[0].Kind, series, points), null, null, notes, source));
    }

    private static ProtonPresentation Kpis(ProtonDataset data, IReadOnlyList<int> chosen, List<string> notes, string id, string title, ProtonBlockSource source)
    {
        var compared = data.Compared && data.Rows.Count == 2;
        if (!compared && data.Rows.Count != 1)
        {
            return Refuse($"Headline figures need one row of figures; this has {data.Rows.Count} rows. Use as=table or as=bar.");
        }

        if (chosen.Count > MaxKpis)
        {
            return Refuse($"Pick at most {MaxKpis} figures with columns.");
        }

        var now = data.Rows[0];
        var before = compared ? data.Rows[1] : null;
        var items = chosen.Select(i =>
        {
            var column = data.Columns[i];
            var value = now.Cells[i].Number;
            var previous = before?.Cells[i].Number;
            return new ProtonKpi(column.Key, column.Label, column.Unit ?? ProtonUnit.Count, value, previous, value - previous);
        }).ToList();
        if (now.Partial)
        {
            notes.Add("The period isn't over: these figures are so far.");
        }

        return Done(new ProtonBlock(id, ProtonBlockType.Kpis, title, data.Period, null, null, new ProtonKpiBlock(items), notes, source));
    }

    private static ProtonPresentation Table(
        ProtonDataset data, IReadOnlyList<int> chosen, ProtonPresentSort? sort, int? top, List<string> notes, string id, string title, ProtonBlockSource source)
    {
        var shownColumns = new[] { 0 }.Concat(chosen.Take(MaxTableColumns - 1)).ToList();
        if (chosen.Count > MaxTableColumns - 1)
        {
            notes.Add($"Shows {MaxTableColumns} of {chosen.Count + 1} columns.");
        }

        var firstNumber = chosen.FirstOrDefault(i => data.Columns[i].IsNumber);
        var rows = sort is null || (sort != ProtonPresentSort.Label && firstNumber == 0) ? data.Rows.ToList() : Sorted(data, firstNumber, sort.Value).ToList();
        var shown = Math.Min(Math.Clamp(top ?? MaxTableRows, 1, MaxTableRows), rows.Count);
        Shown(notes, shown, data.Total, data.Columns[0].Label);
        var columns = shownColumns.Select(i => data.Columns[i]).ToList();
        var table = rows.Take(shown).Select(r => r with { Cells = shownColumns.Select(i => r.Cells[i]).ToList() }).ToList();
        return Done(new ProtonBlock(id, ProtonBlockType.Table, title, data.Period, null, new ProtonTableBlock(columns, table, data.Total), null, notes, source));
    }

    /// <summary>The series of a chart: at most three, all in one unit (a chart never mixes hours with money).</summary>
    private static string? Series(ProtonDataset data, IReadOnlyList<int> chosen, out IReadOnlyList<ProtonSeries> series)
    {
        series = chosen.Select(i => new ProtonSeries(data.Columns[i].Key, data.Columns[i].Label, data.Columns[i].Unit ?? ProtonUnit.Count)).ToList();
        if (series.Count > MaxSeries)
        {
            return $"A chart shows at most {MaxSeries} figures at once.";
        }

        return series.Select(s => s.Unit).Distinct().Count() > 1
            ? "These figures are in different units, so one chart would mislead: show them in separate charts, as=kpis or as=table."
            : null;
    }

    /// <summary>A column as the model names it: its key, in any case, or as the descriptor shows it ("hours (hours)").</summary>
    private static string KeyOf(string named)
    {
        var key = named.Trim().ToLowerInvariant();
        var unit = key.IndexOf(" (", StringComparison.Ordinal);
        return unit > 0 ? key[..unit] : key;
    }

    private static IEnumerable<ProtonDataRow> Sorted(ProtonDataset data, int column, ProtonPresentSort sort) => sort switch
    {
        // a missing value sorts last either way: it isn't a 0
        ProtonPresentSort.ValueDesc => data.Rows.OrderBy(r => r.Cells[column].Number is null).ThenByDescending(r => r.Cells[column].Number),
        ProtonPresentSort.ValueAsc => data.Rows.OrderBy(r => r.Cells[column].Number is null).ThenBy(r => r.Cells[column].Number),
        _ => data.Rows.OrderBy(r => LabelOf(r), StringComparer.CurrentCultureIgnoreCase),
    };

    private static ProtonPoint Point(ProtonDataRow row, IReadOnlyList<int> chosen)
        => new(LabelOf(row), chosen.Select(i => row.Cells[i].Number).ToList(), row.Ref, row.Partial);

    private static string LabelOf(ProtonDataRow row)
        => row.Cells[0].Text ?? row.Cells[0].Date?.ToString("yyyy-MM-dd") ?? row.Cells[0].Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static void Shown(List<string> notes, int shown, int total, string label)
    {
        if (shown < total)
        {
            notes.Add($"Shows {shown} of {total} {Plural(label)}.");
        }
    }

    private static string Plural(string label)
    {
        var word = label.ToLowerInvariant();
        if (word == "person")
        {
            return "people";
        }

        return word.EndsWith('s') ? word : word.EndsWith('y') && !word.EndsWith("ay") ? word[..^1] + "ies" : word + "s";
    }

    /// <summary>The block's title: the model's when it gave one, on one line and short; otherwise the dataset's.</summary>
    public static string TitleOf(string? asked, string fallback)
    {
        var clean = new string((asked ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var line = string.Join(' ', clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var title = line.Length == 0 ? fallback : line;
        return title.Length <= MaxTitle ? title : title[..(MaxTitle - 1)].TrimEnd() + "…";
    }

    private static ProtonPresentation Done(ProtonBlock block) => new(block, null);

    private static ProtonPresentation Refuse(string reason) => new(null, reason);
}
