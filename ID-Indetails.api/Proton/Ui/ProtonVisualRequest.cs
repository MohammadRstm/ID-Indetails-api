using System.Text.Json;
using System.Text.RegularExpressions;
using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>A form the user asked for in their own words.</summary>
public enum ProtonVisualAsk
{
    Table,
    /// <summary>Any chart: "chart", "graph", "plot", "visualise".</summary>
    Chart,
    Bar,
    Line,
    Composition,
}

/// <summary>
/// The user's own request for a chart or table, found in what they typed or said: Proton then shows the data in that
/// form, or says why it can't. Found in words, never guessed; a request it misses is left to the model.
/// </summary>
public static class ProtonVisualRequest
{
    // Most specific first. Only phrasings of a request: a design and building business says "table" (furniture),
    // "bars" (steel), "plot" (land), "visual", "diagram", "مخطط" (a plan) and "جدول زمني" (a schedule) about its work.
    private static readonly (ProtonVisualAsk Ask, Regex Words)[] Asks =
    [
        (ProtonVisualAsk.Composition, Pattern(@"pie( chart)?|donut|doughnut|camembert|(رسم|مخطط) دائري|رسما دائريا|رسمًا دائريًا")),
        (ProtonVisualAsk.Line, Pattern(@"line (chart|graph)s?|trend ?line|courbe|خط بياني|رسم بياني خطي|منحنى")),
        (ProtonVisualAsk.Bar, Pattern(@"bar (chart|graph)s?|histogram(me)?|graphique (à|a|en) barres|(رسم بياني|مخطط) (بالأعمدة|أعمدة)|أعمدة بيانية")),
        (ProtonVisualAsk.Table, Pattern(
            @"(as|in|into) a table|(show|give) me (a|the) table|table (of|showing|comparing)|tabulate|spreadsheet|"
            + @"((sous forme de|en|un|dans un) tableau|tableau (des|du|de la|avec))(?! de bord)|(في|على شكل|بشكل) جدول(?! زمني)|جدولا|جدولاً")),
        (ProtonVisualAsk.Chart, Pattern(@"charts?|graphs?|plot (it|this|that|them|these)|visuali[sz]e|visuali[sz]ation|(un|en|sous forme de) graphique|رسم بياني|رسما بيانيا|رسمًا بيانيًا")),
    ];

    public static ProtonVisualAsk? Detect(string text)
    {
        foreach (var (ask, words) in Asks)
        {
            if (words.IsMatch(text))
            {
                return ask;
            }
        }

        return null;
    }

    /// <summary>Whether present_data was used for the requested form this turn, shown or refused (then the model explains why).</summary>
    public static bool Answered(ProtonVisualAsk ask, IEnumerable<ProtonToolResult> calls)
        => calls.Any(call => call.Tool == "present_data" && FormOf(call.Input) is { } form && Fits(ask, form));

    /// <summary>What the model is told when it answered without the form the user asked for.</summary>
    public static string Reminder(ProtonVisualAsk ask)
        => $"[From the app] The user asked for {Words(ask)}. Show the data that answers the question with present_data "
           + "in that form. If the data can't be shown that way, say why in a sentence and show the closest form that fits.";

    private static bool Fits(ProtonVisualAsk ask, ProtonPresentForm form) => ask switch
    {
        ProtonVisualAsk.Table => form == ProtonPresentForm.Table,
        ProtonVisualAsk.Line => form is ProtonPresentForm.Line or ProtonPresentForm.Columns,
        ProtonVisualAsk.Bar => form is ProtonPresentForm.Bar or ProtonPresentForm.Columns,
        ProtonVisualAsk.Composition => form == ProtonPresentForm.Composition,
        _ => form is not ProtonPresentForm.Table,
    };

    private static ProtonPresentForm? FormOf(JsonElement input)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty("as", out var form) && form.ValueKind == JsonValueKind.String
           && Enum.TryParse<ProtonPresentForm>(form.GetString(), ignoreCase: true, out var parsed) ? parsed : null;

    private static string Words(ProtonVisualAsk ask) => ask switch
    {
        ProtonVisualAsk.Table => "a table",
        ProtonVisualAsk.Line => "a line chart (a trend over time)",
        ProtonVisualAsk.Bar => "a bar chart",
        ProtonVisualAsk.Composition => "a pie chart: the parts of a whole (shown as a composition)",
        _ => "a chart",
    };

    private static Regex Pattern(string words)
        => new($@"(?<![\p{{L}}])({words})(?![\p{{L}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
