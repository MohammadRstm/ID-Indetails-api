using System.ComponentModel;
using System.Text.Json;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Tools;

public sealed record PresentInput
{
    [Description("The id of a dataset a tool returned in this conversation, such as d3 (see datasets in its result).")]
    public string Dataset { get; init; } = string.Empty;

    [Description("bar (ranked categories), columns or line (over time), composition (the parts of a whole), kpis (a few headline figures) or table.")]
    public ProtonPresentForm As { get; init; }

    [Description("The dataset's column keys to show, in order: number columns for a chart or kpis, any for a table. Default: the first figure (all columns for a table, all figures for kpis).")]
    public IReadOnlyList<string>? Columns { get; init; }

    [Description("value_desc, value_asc or label. Bars default to value_desc; time is always in order.")]
    public ProtonPresentSort? Sort { get; init; }

    [Description("Show only the first rows: up to 30 for bars, 60 points over time, 100 for a table.")]
    public int? Top { get; init; }

    [Description("A short title in plain words, at most 80 characters. Default: the dataset's.")]
    public string? Title { get; init; }
}

/// <param name="Shown">What the user now sees, in words, for the model to refer to.</param>
public sealed record PresentOutcome(string Shown, string Block, IReadOnlyList<string> Notes);

/// <summary>
/// Shows a dataset a tool returned in this conversation as a chart, a table or headline figures under the answer. The
/// server builds it from the dataset's own values: the model picks the form and a title, never a number or a label,
/// and the rules refuse a form the data can't honestly take (with the reason and what would fit).
/// </summary>
public sealed class PresentDataTool(IClock clock, IOptions<ProtonOptions> options) : ProtonTool<PresentInput>
{
    /// <summary>A block's size in JSON, at most: what one answer may add to the page and the saved chat.</summary>
    public const int MaxBlockLength = 64 * 1024;

    public override string Name => "present_data";

    public override string Description =>
        "Shows data a tool already returned in this conversation as a chart, a table or headline figures under your " +
        "answer, built from that data by the app. Use it when the user asks for one, or when a comparison of several " +
        "items, a trend over time, the parts of a whole or a list to scan is clearer than words. It reads nothing new.";

    // like the other UI tools: no data of its own, open to whoever may use Proton's lists
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override Task<ProtonToolOutput> RunAsync(PresentInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var visuals = options.Value.Visuals;
        if (!visuals.Enabled)
        {
            throw new ProtonToolInputException("Charts and tables aren't switched on: answer in words.");
        }

        if (context.BlocksShown >= visuals.MaxBlocksPerAnswer)
        {
            throw new ProtonToolInputException($"An answer shows at most {visuals.MaxBlocksPerAnswer} charts or tables: say the rest in words.");
        }

        var held = context.Dataset(input.Dataset.Trim())
            ?? throw new ProtonToolInputException($"There is no dataset \"{input.Dataset}\" in this conversation: read the data with a tool first.");
        if (clock.UtcNow - held.AsOf > TimeSpan.FromMinutes(visuals.DatasetMinutes))
        {
            throw new ProtonToolInputException($"That data was read more than {visuals.DatasetMinutes} minutes ago: read it again before showing it.");
        }

        var presentation = ProtonPresentationRules.Present(
            held, input.As, input.Columns, input.Sort, input.Top, input.Title, $"b{context.BlocksShown + 1}-{held.Id}");
        if (presentation.Block is not { } block)
        {
            throw new ProtonToolInputException(presentation.Refusal!);
        }

        if (JsonSerializer.Serialize(block, ProtonJson.Options).Length > MaxBlockLength)
        {
            throw new ProtonToolInputException("That is too much to show at once: use top to show fewer rows.");
        }

        context.BlocksShown++;
        var shown = block.Type switch
        {
            ProtonBlockType.Table => $"a table, \"{block.Title}\", with {block.Table!.Rows.Count} rows",
            ProtonBlockType.Kpis => $"{block.Kpis!.Items.Count} headline figures, \"{block.Title}\"",
            _ => $"a {Words(block.Chart!.Kind)}, \"{block.Title}\", with {block.Chart.Points.Count} points",
        };
        var outcome = new PresentOutcome(
            $"The user now sees {shown} under your answer. Your answer still says what matters in words; don't repeat every value.",
            block.Id, block.Notes);
        return Task.FromResult(new ProtonToolOutput(outcome, []) { Block = block });
    }

    private static string Words(ProtonPresentForm form) => form switch
    {
        ProtonPresentForm.Bar => "bar chart",
        ProtonPresentForm.Columns => "column chart over time",
        ProtonPresentForm.Line => "line chart",
        ProtonPresentForm.Composition => "composition chart",
        _ => "chart",
    };
}
