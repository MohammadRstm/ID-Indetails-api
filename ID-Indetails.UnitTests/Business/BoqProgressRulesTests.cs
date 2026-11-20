using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using static ID_Indetails.api.Business.Boqs.BoqProgressRules;

namespace ID_Indetails.UnitTests.Business;

public class BoqProgressRulesTests
{
    /// <summary>A line of the agreed client BOQ: 100 m² at 10, in a BOQ of 1,000 taken down to 900 by its adjustment.</summary>
    private static readonly Line Tiling = new(1, 10, BoqKind.Client, BoqStatus.Agreed, "Client BOQ", 1_000m, 900m, BoqLineNature.Work, 100m, 0m, 10m);

    /// <summary>An agreed VO taking 10 m² away at 10.</summary>
    private static readonly Line LessTiling = new(2, 11, BoqKind.Vo, BoqStatus.Agreed, "Less tiling", -100m, -100m, BoqLineNature.Work, 10m, 0m, -10m);

    /// <summary>A permit on the client BOQ: not work.</summary>
    private static readonly Line Permit = Tiling with { Id = 3, Nature = BoqLineNature.Permit, Quantity = 1m, Rate = 500m };

    /// <summary>The VO that lowered the price when what was owed was excused at completion.</summary>
    private static readonly Line Excused = new(4, 12, BoqKind.Vo, BoqStatus.Agreed, BoqRules.ExcusedTitle, -300m, -300m, BoqLineNature.Work, 1m, 0m, -300m);

    private static IReadOnlyDictionary<long, Line> Site(params Line[] lines) => lines.ToDictionary(l => l.Id);

    [Fact]
    public void Only_work_lines_of_agreed_client_boqs_and_vos_count_and_settlements_and_drafts_never()
    {
        Counts(Tiling).ShouldBeTrue();
        Counts(LessTiling).ShouldBeTrue();
        Counts(Permit).ShouldBeFalse();
        Counts(Excused).ShouldBeFalse();
        Counts(Tiling with { BoqTitle = BoqRules.ClosingTitle }).ShouldBeFalse();
        Counts(Tiling with { Status = BoqStatus.Draft }).ShouldBeFalse();
        Counts(Tiling with { Kind = BoqKind.Internal }).ShouldBeFalse();

        // a line taking work away counts in the agreed value but has nothing to execute
        TakesProgress(Tiling).ShouldBeTrue();
        TakesProgress(LessTiling).ShouldBeFalse();
        TakesProgress(Tiling with { Rate = 0m }).ShouldBeTrue();
    }

    [Fact]
    public void The_quantity_is_the_final_one_with_waste_and_up_to_twice_it_may_be_recorded()
    {
        var withWaste = Tiling with { WasteRate = 0.05m };
        QuantityOf(withWaste).ShouldBe(105m);
        MostExecuted(QuantityOf(withWaste)).ShouldBe(210m);

        CheckEntries([new Entry(1, 210m)], Site(withWaste)).ShouldBeNull();
        CheckEntries([new Entry(1, 0m)], Site(withWaste)).ShouldBeNull();
        var typo = CheckEntries([new Entry(1, 210.0001m)], Site(withWaste)).ShouldNotBeNull();
        (typo.Kind, typo.Code, typo.Field).ShouldBe((DenialKind.Validation, ErrorCodes.ValidationFailed, "lines[0].executedQuantity"));
    }

    [Fact]
    public void A_line_that_takes_no_progress_is_refused_with_its_reason()
    {
        void Refused(Line? line, long id, string because)
        {
            var denial = CheckEntries([new Entry(id, 1m)], line is null ? Site() : Site(line)).ShouldNotBeNull();
            (denial.Kind, denial.Code, denial.Field).ShouldBe((DenialKind.Validation, ErrorCodes.BoqProgressLineInvalid, "lines[0].boqLineId"));
            denial.Message.ShouldContain(because);
        }

        // not on the site's agreed BOQs: another site's line, or a draft's
        Refused(null, 1, "agreed BOQ");
        Refused(Tiling with { Status = BoqStatus.Draft }, 1, "agreed BOQ");
        Refused(Excused, 4, "completed");
        Refused(Permit, 3, "permit");
        Refused(LessTiling, 2, "takes work away");
    }

    [Fact]
    public void Each_line_is_recorded_once_and_the_error_points_at_the_line_at_fault()
    {
        var twice = CheckEntries([new Entry(1, 5m), new Entry(1, 6m)], Site(Tiling)).ShouldNotBeNull();
        (twice.Kind, twice.Field).ShouldBe((DenialKind.Validation, "lines"));

        var second = CheckEntries([new Entry(1, 5m), new Entry(9, 6m)], Site(Tiling)).ShouldNotBeNull();
        second.Field.ShouldBe("lines[1].boqLineId");
    }

    [Fact]
    public void Progress_is_recorded_up_to_today_and_never_for_a_day_to_come()
    {
        var today = new DateOnly(2026, 11, 11);
        CheckDay(today, today).ShouldBeNull();
        CheckDay(today.AddDays(-30), today).ShouldBeNull();
        var future = CheckDay(today.AddDays(1), today).ShouldNotBeNull();
        (future.Kind, future.Field).ShouldBe((DenialKind.Validation, "recordedOn"));
    }

    [Fact]
    public void A_lines_value_is_at_its_rate_with_the_boqs_adjustment_spread_over_it()
    {
        // 10 × 900 / 1,000 = 9: the BOQ fully executed is worth its agreed 900
        var half = FiguresOf(Tiling, 50m);
        (half.Quantity, half.Executed, half.Progress, half.UnitPrice, half.AgreedValue, half.ValueExecuted)
            .ShouldBe((100m, (decimal?)50m, (decimal?)0.5m, 9m, 900m, 450m));

        // no adjustment: the rate itself, so the agreed value is the line's total
        var plain = FiguresOf(Tiling with { BoqTotal = 1_000m }, null);
        (plain.UnitPrice, plain.AgreedValue, plain.ValueExecuted, plain.Executed, plain.Progress).ShouldBe((10m, 1_000m, 0m, (decimal?)null, (decimal?)null));

        // a BOQ with no subtotal keeps its rates
        AdjustedRate(5m, 0m, 0m).ShouldBe(5m);
        // a line with no quantity has no progress, whatever is recorded
        FiguresOf(Tiling with { Quantity = 0m }, 0m).Progress.ShouldBeNull();
    }

    [Fact]
    public void Progress_is_value_executed_over_agreed_value_with_deductions_lowering_the_agreed_value()
    {
        var sheet = Of([Tiling, LessTiling, Permit, Excused], new Dictionary<long, decimal> { [1] = 50m });

        // the permit and the settlement are left out; the deduction lowers the agreed value: 900 − 100
        sheet.Lines.Select(l => l.Line.Id).ShouldBe([1L, 2L]);
        (sheet.AgreedValue, sheet.ValueExecuted, sheet.Progress, sheet.Recorded).ShouldBe((800m, 450m, (decimal?)0.5625m, true));
        sheet.Lines[1].ShouldSatisfyAllConditions(
            l => l.AgreedValue.ShouldBe(-100m),
            l => l.Executed.ShouldBeNull(),
            l => l.ValueExecuted.ShouldBe(0m));

        // the 90 m² left after the VO, all executed, is the whole agreed value
        Of([Tiling with { BoqTotal = 1_000m }, LessTiling], new Dictionary<long, decimal> { [1] = 90m }).Progress.ShouldBe(1m);
    }

    [Fact]
    public void An_overrun_takes_progress_past_one_and_nothing_recorded_means_no_progress()
    {
        var over = Of([Tiling], new Dictionary<long, decimal> { [1] = 200m });
        (over.ValueExecuted, over.Progress).ShouldBe((1_800m, (decimal?)2m));
        over.Lines.Single().Progress.ShouldBe(2m);

        var none = Of([Tiling], new Dictionary<long, decimal>());
        (none.Recorded, none.ValueExecuted, none.Progress).ShouldBe((false, 0m, (decimal?)0m));

        // an agreed value of nothing or less has no progress
        Of([LessTiling], new Dictionary<long, decimal>()).Progress.ShouldBeNull();
        Of([Excused], new Dictionary<long, decimal> { [4] = 1m }).ShouldSatisfyAllConditions(
            s => s.Lines.ShouldBeEmpty(), s => s.Recorded.ShouldBeFalse(), s => s.Progress.ShouldBeNull());
    }

    [Fact]
    public void Progress_is_rounded_to_four_places_and_values_to_cents()
    {
        // 1 of 3 at 1: 0.3333; 1 × (10 × 2 / 3) = 6.67
        var third = Tiling with { Quantity = 3m, Rate = 10m, BoqSubtotal = 30m, BoqTotal = 20m };
        var figures = FiguresOf(third, 1m);
        figures.Progress.ShouldBe(0.3333m);
        figures.UnitPrice.ShouldBe(6.6667m);
        figures.ValueExecuted.ShouldBe(6.67m);
        figures.AgreedValue.ShouldBe(20m);
    }
}
