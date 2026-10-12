using System.Text.Json.Serialization;
using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>What a figure counts. Charts never mix units; a group of headline figures may.</summary>
public enum ProtonUnit
{
    Hours,
    Usd,
    Count,
    Percent,
    Days,
}

/// <summary>What a dataset column holds. The first column names the rows: a text, a status, or a day, week or month.</summary>
public enum ProtonColumnKind
{
    Text,
    Status,
    Date,
    Week,
    Month,
    Number,
}

public sealed record ProtonColumn(string Key, string Label, ProtonColumnKind Kind, ProtonUnit? Unit = null)
{
    [JsonIgnore]
    public bool IsNumber => Kind == ProtonColumnKind.Number;

    [JsonIgnore]
    public bool IsTime => Kind is ProtonColumnKind.Date or ProtonColumnKind.Week or ProtonColumnKind.Month;
}

/// <summary>One value: a text, a number or a day, as its column says. A missing value is null, never 0.</summary>
public sealed record ProtonCell(string? Text = null, decimal? Number = null, DateOnly? Date = null)
{
    public static ProtonCell Of(string? text) => new(Text: text);

    public static ProtonCell Of(decimal? number) => new(Number: number);

    public static ProtonCell Of(DateOnly? date) => new(Date: date);
}

/// <param name="Ref">The record the row is about, for a link.</param>
/// <param name="Partial">The row's period isn't over (this week, this month), so its figures are "so far".</param>
public sealed record ProtonDataRow(IReadOnlyList<ProtonCell> Cells, ProtonRef? Ref = null, bool Partial = false);

/// <summary>
/// A tool's result as a table of typed values, built by the tool's code from the same data it returned to the model:
/// what a chart, a table or headline figures may be made of. The model never writes these values.
/// </summary>
/// <param name="Period">The days it covers, in words, when it covers a period.</param>
/// <param name="TotalRows">How many rows there are in all; <paramref name="Rows"/> may hold only the first of them.</param>
/// <param name="PartsOfWhole">Its rows add up to a whole (cost by source, projects by status): a composition can show it.</param>
/// <param name="Compared">Headline figures: the first row is the period, the second the one it is compared with.</param>
/// <param name="Notes">What a reader must know about it: a partial period, what was left out, an unconverted currency.</param>
public sealed record ProtonDataset(
    string Title,
    IReadOnlyList<ProtonColumn> Columns,
    IReadOnlyList<ProtonDataRow> Rows,
    string? Period = null,
    int? TotalRows = null,
    bool PartsOfWhole = false,
    bool Compared = false,
    IReadOnlyList<string>? Notes = null)
{
    public int Total => TotalRows ?? Rows.Count;

    public bool Complete => Total <= Rows.Count;
}

/// <summary>A dataset this conversation holds, with where it came from.</summary>
public sealed record ProtonHeldDataset(string Id, string Tool, ProtonDataset Dataset, DateTime AsOf);

/// <summary>The forms a dataset can be shown in.</summary>
public enum ProtonPresentForm
{
    /// <summary>Ranked horizontal bars: categories compared.</summary>
    Bar,

    /// <summary>Vertical bars over time.</summary>
    Columns,

    /// <summary>Lines over time.</summary>
    Line,

    /// <summary>One strip split into the parts of a whole.</summary>
    Composition,

    /// <summary>A few headline figures.</summary>
    Kpis,

    Table,
}

public enum ProtonBlockType
{
    Chart,
    Table,
    Kpis,
}

/// <summary>
/// A chart, table or headline figures shown with an answer, built by the server from a dataset a tool returned in
/// this conversation. Every value, label and note comes from the data; the model chose only the form and the title.
/// </summary>
public sealed record ProtonBlock(
    string Id,
    ProtonBlockType Type,
    string Title,
    string? Period,
    ProtonChartBlock? Chart,
    ProtonTableBlock? Table,
    ProtonKpiBlock? Kpis,
    IReadOnlyList<string> Notes,
    ProtonBlockSource Source);

/// <param name="Axis">What the points are: categories, or days, weeks or months in order.</param>
public sealed record ProtonChartBlock(ProtonPresentForm Kind, string AxisLabel, ProtonColumnKind Axis, IReadOnlyList<ProtonSeries> Series, IReadOnlyList<ProtonPoint> Points);

public sealed record ProtonSeries(string Key, string Label, ProtonUnit Unit);

/// <param name="Values">One per series, in order; null where there is no value.</param>
public sealed record ProtonPoint(string Label, IReadOnlyList<decimal?> Values, ProtonRef? Ref = null, bool Partial = false);

/// <param name="TotalRows">How many rows the data has; the table may show only the first of them.</param>
public sealed record ProtonTableBlock(IReadOnlyList<ProtonColumn> Columns, IReadOnlyList<ProtonDataRow> Rows, int TotalRows);

public sealed record ProtonKpiBlock(IReadOnlyList<ProtonKpi> Items);

/// <param name="Change">The value less the previous one, worked out by the server.</param>
public sealed record ProtonKpi(string Key, string Label, ProtonUnit Unit, decimal? Value, decimal? Previous = null, decimal? Change = null);

/// <summary>Where a block's figures came from: the tool, the dataset, and when they were read.</summary>
public sealed record ProtonBlockSource(string Tool, string Dataset, DateTime AsOf);
