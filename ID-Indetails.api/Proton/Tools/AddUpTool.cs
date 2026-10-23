using System.ComponentModel;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Tools;

public sealed record AddUpInput
{
    [Description("The id of a dataset a tool returned in this conversation, such as d3 (see datasets in its result).")]
    public string Dataset { get; init; } = string.Empty;

    [Description("The key of the number column to total.")]
    public string Column { get; init; } = string.Empty;

    [Description("To total only some rows: the key of a text or status column to match.")]
    public string? WhereColumn { get; init; }

    [Description("With where_column: the value it must have (e.g. a client's name, a status), as the dataset spells it.")]
    public string? WhereValue { get; init; }
}

/// <param name="Rows">The rows added up.</param>
/// <param name="RowsWithoutValue">Rows that matched but have no value in the column: left out, not counted as zero.</param>
/// <param name="SoFar">A row's period isn't over, so the total is so far.</param>
public sealed record AddUpResult(
    string Dataset, string Column, string Unit, decimal Total, int Rows, int RowsWithoutValue, bool SoFar, string? Period, IReadOnlyList<string>? Notes);

/// <summary>
/// Totals a number column of a dataset a tool returned in this conversation, so the model never adds figures up itself.
/// The server sums the dataset's own values: only a complete dataset, never percentages, and never two periods' headline
/// figures together.
/// </summary>
public sealed class AddUpTool(IClock clock, IOptions<ProtonOptions> options) : ProtonTool<AddUpInput>
{
    public override string Name => "add_up";

    public override string Description =>
        "Totals a number column of data a tool already returned in this conversation (its dataset), optionally only the " +
        "rows where a text column has a value: e.g. the payments of one client, the invoices of a status. Use it instead " +
        "of adding figures yourself. It reads nothing new.";

    // like present_data: no data of its own, only datasets the user's own tools returned
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override Task<ProtonToolOutput> RunAsync(AddUpInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var held = context.Dataset(input.Dataset.Trim())
            ?? throw new ProtonToolInputException($"There is no dataset \"{input.Dataset}\" in this conversation: read the data with a tool first.");
        var minutes = options.Value.Visuals.DatasetMinutes;
        if (clock.UtcNow - held.AsOf > TimeSpan.FromMinutes(minutes))
        {
            throw new ProtonToolInputException($"That data was read more than {minutes} minutes ago: read it again before adding it up.");
        }

        var data = held.Dataset;
        var column = Column(data, input.Column);
        if (column is not { IsNumber: true })
        {
            throw new ProtonToolInputException($"\"{input.Column}\" isn't a number column of {held.Id}. Its number columns: {string.Join(", ", data.Columns.Where(c => c.IsNumber).Select(c => c.Key))}.");
        }

        if (column.Unit == ProtonUnit.Percent)
        {
            throw new ProtonToolInputException("Percentages don't add up: use a tool that gives the percentage of the whole.");
        }

        if (data.Compared)
        {
            throw new ProtonToolInputException("These are the headline figures of two periods side by side: they don't add up.");
        }

        if (!data.Complete)
        {
            throw new ProtonToolInputException(
                $"{held.Id} holds only the first {data.Rows.Count} of {data.Total} rows, so its total would be wrong: narrow the question " +
                "(a period, a client, a project) and read it again, or use a tool that totals it.");
        }

        var rows = data.Rows.AsEnumerable();
        if (input.WhereColumn is { } whereKey)
        {
            var where = Column(data, whereKey);
            if (where is not { Kind: ProtonColumnKind.Text or ProtonColumnKind.Status })
            {
                throw new ProtonToolInputException(
                    $"\"{whereKey}\" isn't a text column of {held.Id}. Its text columns: {string.Join(", ", data.Columns.Where(c => c.Kind is ProtonColumnKind.Text or ProtonColumnKind.Status).Select(c => c.Key))}.");
            }

            var at = Index(data, where);
            rows = rows.Where(r => string.Equals(r.Cells[at].Text?.Trim(), input.WhereValue?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var index = Index(data, column);
        var matched = rows.ToList();
        var values = matched.Select(r => r.Cells[index].Number).ToList();
        var result = new AddUpResult(
            data.Title,
            column.Label,
            column.Unit?.ToString().ToLowerInvariant() ?? "number",
            Math.Round(values.Sum(v => v ?? 0), 2, MidpointRounding.AwayFromZero),
            values.Count(v => v is not null),
            values.Count(v => v is null),
            matched.Any(r => r.Partial),
            data.Period,
            data.Notes);
        return Task.FromResult(new ProtonToolOutput(result, matched.Select(r => r.Ref).OfType<ProtonRef>().Distinct().ToList()));
    }

    private static ProtonColumn? Column(ProtonDataset data, string key)
        => data.Columns.FirstOrDefault(c => string.Equals(c.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    private static int Index(ProtonDataset data, ProtonColumn column) => data.Columns.ToList().IndexOf(column);
}
