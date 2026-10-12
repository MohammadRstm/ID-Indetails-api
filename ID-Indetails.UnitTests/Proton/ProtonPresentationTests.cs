using System.Text.Json;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>
/// Charts, tables and headline figures: a form is shown only when the data can honestly take it, every value comes
/// from the dataset, and what was left out is said under the block.
/// </summary>
public class ProtonPresentationTests
{
    private static readonly DateTime AsOf = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static ProtonColumn Hours(string key = "hours", string label = "Hours") => ProtonDatasets.Figure(key, label, ProtonUnit.Hours);

    private static ProtonDataset ByPerson(int people, bool partsOfWhole = true, int? total = null) => new(
        "Hours by person",
        [ProtonDatasets.Label("person", "Person"), Hours(), ProtonDatasets.Figure("entries", "Entries", ProtonUnit.Count)],
        Enumerable.Range(1, people).Select(i => ProtonDatasets.Row(
            new ProtonRef(ProtonRefType.Person, i, $"Person {i}"), ProtonCell.Of($"Person {i}"), ProtonCell.Of(i * 2m), ProtonCell.Of(i))).ToList(),
        "1 Oct 2026 – 12 Oct 2026",
        total,
        PartsOfWhole: partsOfWhole);

    private static ProtonDataset Weekly(int weeks, bool lastPartial = true) => new(
        "Hours by week",
        [ProtonDatasets.Label("week", "Week", ProtonColumnKind.Week), Hours()],
        Enumerable.Range(0, weeks).Select(i => new ProtonDataRow(
            [ProtonCell.Of(new DateOnly(2026, 9, 7).AddDays(7 * i)), ProtonCell.Of(10m + i)], Partial: lastPartial && i == weeks - 1)).ToList());

    private static ProtonPresentation Show(
        ProtonDataset data, ProtonPresentForm form, IReadOnlyList<string>? columns = null, ProtonPresentSort? sort = null, int? top = null, string? title = null)
        => ProtonPresentationRules.Present(new ProtonHeldDataset("d1", "get_time_summary", data, AsOf), form, columns, sort, top, title, "b1-d1");

    [Fact]
    public void A_bar_chart_ranks_the_rows_and_says_how_many_it_leaves_out()
    {
        var shown = Show(ByPerson(5, total: 9), ProtonPresentForm.Bar, top: 3).Block.ShouldNotBeNull();

        shown.Type.ShouldBe(ProtonBlockType.Chart);
        shown.Chart!.Points.Select(p => p.Label).ShouldBe(["Person 5", "Person 4", "Person 3"]);
        shown.Chart.Points[0].Values.ShouldBe([10m]);
        shown.Chart.Points[0].Ref!.Id.ShouldBe(5);
        shown.Chart.Series.Single().Unit.ShouldBe(ProtonUnit.Hours);
        shown.Notes.ShouldContain("Shows 3 of 9 people.");
        shown.Source.ShouldBe(new ProtonBlockSource("get_time_summary", "d1", AsOf));
    }

    [Fact]
    public void A_missing_value_stays_missing_and_sorts_last()
    {
        var data = ByPerson(3) with
        {
            Rows = [.. ByPerson(3).Rows.Take(2), ProtonDatasets.Row(null, ProtonCell.Of("Nobody yet"), ProtonCell.Of((decimal?)null), ProtonCell.Of(0m))],
        };

        var chart = Show(data, ProtonPresentForm.Bar, sort: ProtonPresentSort.ValueAsc).Block!.Chart!;

        chart.Points.Select(p => p.Label).ShouldBe(["Person 1", "Person 2", "Nobody yet"]);
        chart.Points[^1].Values.ShouldBe([null]);
    }

    [Fact]
    public void Over_time_needs_points_in_time_and_keeps_them_in_order()
    {
        Show(ByPerson(4), ProtonPresentForm.Line).Refusal!.ShouldContain("needs points in time");
        Show(Weekly(4), ProtonPresentForm.Bar).Refusal!.ShouldContain("use as=columns or as=line");
        Show(Weekly(2), ProtonPresentForm.Columns).Refusal!.ShouldContain("at least 3 points in time");

        var reversed = Weekly(5) with { Rows = Weekly(5).Rows.Reverse().ToList() };
        var line = Show(reversed, ProtonPresentForm.Line, top: 4).Block!;

        line.Chart!.Points.Select(p => p.Label).ShouldBe(["2026-09-14", "2026-09-21", "2026-09-28", "2026-10-05"]);
        line.Chart.Points[^1].Partial.ShouldBeTrue();
        line.Notes.ShouldContain("Shows the latest 4 of 5 weeks.");
        line.Notes.ShouldContain("The last week isn't over: its figures are so far.");
    }

    [Fact]
    public void A_composition_needs_every_part_of_one_whole()
    {
        Show(ByPerson(4), ProtonPresentForm.Composition).Block.ShouldNotBeNull().Chart!.Kind.ShouldBe(ProtonPresentForm.Composition);

        Show(ByPerson(4, partsOfWhole: false), ProtonPresentForm.Composition).Refusal!.ShouldContain("aren't the parts of one whole");
        Show(ByPerson(4, total: 6), ProtonPresentForm.Composition).Refusal!.ShouldContain("Only 4 of 6 parts");
        Show(ByPerson(9), ProtonPresentForm.Composition).Refusal!.ShouldContain("2 to 8 parts");
        Show(ByPerson(4), ProtonPresentForm.Composition, ["hours", "entries"]).Refusal!.ShouldContain("pick one column");

        var negative = ByPerson(3) with { Rows = [.. ByPerson(3).Rows, ProtonDatasets.Row(null, ProtonCell.Of("Refund"), ProtonCell.Of(-4m), ProtonCell.Of(1m))] };
        Show(negative, ProtonPresentForm.Composition).Refusal!.ShouldContain("below zero");
    }

    [Fact]
    public void A_chart_never_mixes_units()
    {
        Show(ByPerson(3), ProtonPresentForm.Bar, ["hours", "entries"]).Refusal!.ShouldContain("different units");
        Show(ByPerson(3), ProtonPresentForm.Bar, ["person"]).Refusal!.ShouldContain("There is no number column \"person\"");
        Show(ByPerson(3), ProtonPresentForm.Bar, ["cost"]).Refusal!.ShouldContain("Its number columns are: hours, entries.");
    }

    [Fact]
    public void Headline_figures_of_two_periods_carry_the_change_worked_out_here()
    {
        var pair = new ProtonPeriodPair(new DayRange(new(2026, 10, 1), new(2026, 10, 12)), new DayRange(new(2026, 9, 1), new(2026, 9, 12)), true, "Like for like: the first 12 days of each month.");
        var data = ProtonDatasets.Compared("Hours compared", pair, [Hours(), ProtonDatasets.Figure("people", "People", ProtonUnit.Count)], [120m, 6m], [100m, null]);

        var kpis = Show(data, ProtonPresentForm.Kpis).Block!;

        kpis.Type.ShouldBe(ProtonBlockType.Kpis);
        kpis.Kpis!.Items[0].ShouldBe(new ProtonKpi("hours", "Hours", ProtonUnit.Hours, 120m, 100m, 20m));
        kpis.Kpis.Items[1].Change.ShouldBeNull();
        kpis.Notes.ShouldContain("Like for like: the first 12 days of each month.");
        kpis.Notes.ShouldContain("The period isn't over: these figures are so far.");
        Show(ByPerson(3), ProtonPresentForm.Kpis).Refusal!.ShouldContain("one row of figures");
    }

    [Fact]
    public void A_table_keeps_its_columns_in_order_and_counts_what_it_leaves_out()
    {
        var table = Show(ByPerson(5, total: 140), ProtonPresentForm.Table, ["entries"], ProtonPresentSort.ValueDesc, 2).Block!;

        table.Type.ShouldBe(ProtonBlockType.Table);
        table.Table!.Columns.Select(c => c.Key).ShouldBe(["person", "entries"]);
        table.Table.Rows.Select(r => r.Cells[0].Text).ShouldBe(["Person 5", "Person 4"]);
        table.Table.TotalRows.ShouldBe(140);
        table.Notes.ShouldContain("Shows 2 of 140 people.");
    }

    [Fact]
    public void The_forms_offered_are_the_ones_the_data_fits()
    {
        ProtonPresentationRules.FormsFor(ByPerson(4)).ShouldBe(
            [ProtonPresentForm.Bar, ProtonPresentForm.Composition, ProtonPresentForm.Table]);
        ProtonPresentationRules.FormsFor(Weekly(6)).ShouldBe(
            [ProtonPresentForm.Columns, ProtonPresentForm.Line, ProtonPresentForm.Table]);
        ProtonPresentationRules.FormsFor(ByPerson(1)).ShouldBe([ProtonPresentForm.Kpis, ProtonPresentForm.Table]);
    }

    [Theory]
    [InlineData(null, "Hours by person")]
    [InlineData("  Who\nlogged most\t ", "Who logged most")]
    public void The_title_is_one_short_line(string? asked, string expected)
        => ProtonPresentationRules.TitleOf(asked, "Hours by person").ShouldBe(expected);

    [Fact]
    public void A_long_title_is_cut_at_the_limit()
    {
        var title = ProtonPresentationRules.TitleOf(new string('a', 200), "x");

        title.Length.ShouldBe(ProtonPresentationRules.MaxTitle);
        title.ShouldEndWith("…");
    }

    [Fact]
    public void Weekly_bars_say_when_they_reach_before_the_period_and_mark_the_week_not_over()
    {
        var weeks = new[] { (new DateOnly(2026, 9, 28), 30m, 10), (new DateOnly(2026, 10, 5), 32m, 11), (new DateOnly(2026, 10, 12), 4m, 2) };

        var data = ProtonDatasets.Weeks("Hours by week", weeks, new(2026, 10, 1), new(2026, 10, 12), today: new(2026, 10, 12));

        data.Notes!.ShouldContain(n => n.Contains("weeks before 1 Oct 2026 are shown too"));
        data.Rows.Select(r => r.Partial).ShouldBe([false, false, true]);
        data.Period.ShouldBe("1 Oct 2026 – 12 Oct 2026");

        var past = ProtonDatasets.Weeks("Hours by week", weeks, new(2026, 9, 28), new(2026, 10, 14), today: new(2026, 10, 20));
        past.Notes!.ShouldBe(["The last week counts only the days up to 14 Oct 2026."]);
        past.Rows.ShouldAllBe(r => !r.Partial);
    }

    [Theory]
    [InlineData("Show me hours by person as a table", ProtonVisualAsk.Table)]
    [InlineData("can you tabulate that", ProtonVisualAsk.Table)]
    [InlineData("a pie chart of cost by source", ProtonVisualAsk.Composition)]
    [InlineData("hours per week as a line chart", ProtonVisualAsk.Line)]
    [InlineData("bar chart of the projects", ProtonVisualAsk.Bar)]
    [InlineData("Chart it", ProtonVisualAsk.Chart)]
    [InlineData("can you visualise this?", ProtonVisualAsk.Chart)]
    [InlineData("montre-moi ça sous forme de tableau", ProtonVisualAsk.Table)]
    [InlineData("fais un graphique des heures", ProtonVisualAsk.Chart)]
    [InlineData("اعرضها في جدول", ProtonVisualAsk.Table)]
    [InlineData("ارسم لي رسم بياني للساعات", ProtonVisualAsk.Chart)]
    [InlineData("رسم دائري للتكاليف", ProtonVisualAsk.Composition)]
    public void A_request_for_a_chart_or_table_is_found_in_the_users_words(string text, ProtonVisualAsk expected)
        => ProtonVisualRequest.Detect(text).ShouldBe(expected);

    /// <summary>Words the business uses about its own work aren't requests for a chart.</summary>
    [Theory]
    [InlineData("Did the dining table for Villa Mounir arrive?")]
    [InlineData("How many steel bars are left on site?")]
    [InlineData("What's the plot area of the Acme project?")]
    [InlineData("Is the visual identity for Acme done?")]
    [InlineData("Send the bubble diagram to the client")]
    [InlineData("Ouvre le tableau de bord")]
    [InlineData("la charte graphique est prête ?")]
    [InlineData("ما هو الجدول الزمني للمشروع؟")]
    [InlineData("هل وصل الدرج الدائري؟")]
    [InlineData("أرسل المخطط إلى العميل")]
    [InlineData("comfortable?")]
    public void Ordinary_words_of_the_business_ask_for_nothing(string text)
        => ProtonVisualRequest.Detect(text).ShouldBeNull();

    [Fact]
    public void A_request_is_answered_by_present_data_in_a_fitting_form_shown_or_refused()
    {
        static ProtonToolResult Call(string tool, string form, ProtonToolStatus status = ProtonToolStatus.Ok)
            => new(tool, JsonSerializer.SerializeToElement(new { dataset = "d1", @as = form }), status, "", [], null, 0);

        ProtonVisualRequest.Answered(ProtonVisualAsk.Table, [Call("present_data", "table")]).ShouldBeTrue();
        ProtonVisualRequest.Answered(ProtonVisualAsk.Table, [Call("present_data", "bar")]).ShouldBeFalse();
        ProtonVisualRequest.Answered(ProtonVisualAsk.Line, [Call("present_data", "columns", ProtonToolStatus.Invalid)]).ShouldBeTrue();
        ProtonVisualRequest.Answered(ProtonVisualAsk.Chart, [Call("present_data", "composition")]).ShouldBeTrue();
        ProtonVisualRequest.Answered(ProtonVisualAsk.Chart, [Call("get_time_summary", "bar")]).ShouldBeFalse();
        ProtonVisualRequest.Reminder(ProtonVisualAsk.Composition).ShouldContain("present_data");
    }
}
