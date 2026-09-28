using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Statements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class StatementRulesTests
{
    private static readonly DateTime Day = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly StatementRules.Material[] Materials =
    [
        new(1, "Cement", 40, "bag", 330, IsStated: true, Day),
        new(2, "Rebar", 2, "tonne", 3300, IsStated: true, Day.AddDays(1)),
        new(3, "cement ", 10, "bag", 82.5m, IsStated: false, Day.AddDays(2)),
        new(4, "Tiles", null, null, 1100, IsStated: false, Day.AddDays(3)),
    ];

    [Fact]
    public void Statement_numbers_are_one_sequence_per_year()
    {
        StatementRules.Number(2026, 7).ShouldBe("ST-2026-007");
        StatementRules.Number(2027, 1234).ShouldBe("ST-2027-1234");
    }

    [Fact]
    public void A_detailed_statement_takes_the_new_materials_not_left_out()
    {
        var (lines, denial) = StatementRules.Build(Materials, Choice(StatementMode.Detail, excluded: [4]));

        denial.ShouldBeNull();
        lines.ShouldBe([new StatementRules.Line(3, 1, "cement ", 10, "bag", 82.5m, IsEarlier: false, IsListed: true)]);
    }

    [Fact]
    public void A_total_only_statement_takes_every_new_material()
    {
        var (lines, _) = StatementRules.Build(Materials, Choice(StatementMode.Total, excluded: [4]));

        lines.Select(l => l.MaterialId).ShouldBe([3, 4]);
    }

    [Fact]
    public void Earlier_materials_come_back_in_date_order_and_types_with_nothing_new_can_be_unlisted()
    {
        var (lines, denial) = StatementRules.Build(Materials, Choice(StatementMode.Detail, includeEarlier: true, unlisted: ["rebar"]));

        denial.ShouldBeNull();
        lines.Select(l => (l.MaterialId, l.Position, l.IsEarlier, l.IsListed))
            .ShouldBe([(1, 1, true, true), (2, 2, true, false), (3, 3, false, true), (4, 4, false, true)]);
    }

    [Fact]
    public void A_type_with_new_materials_is_always_listed()
    {
        var (_, denial) = StatementRules.Build(Materials, Choice(StatementMode.Detail, includeEarlier: true, unlisted: ["cement"]));

        denial!.Kind.ShouldBe(DenialKind.Validation);
    }

    [Fact]
    public void A_statement_needs_at_least_one_material()
    {
        var (_, denial) = StatementRules.Build(Materials, Choice(StatementMode.Detail, excluded: [3, 4]));

        denial!.Code.ShouldBe(ErrorCodes.StatementEmpty);
    }

    [Fact]
    public void The_document_has_one_row_per_type_splitting_earlier_from_new()
    {
        var (lines, _) = StatementRules.Build(Materials, Choice(StatementMode.Detail, includeEarlier: true, unlisted: ["rebar"]));

        var document = StatementRules.Summarise(StatementMode.Detail, lines);

        document.Types.ShouldBe([
            new StatementRules.TypeRow("cement", "Cement", "50 bag", 2, Earlier: 330, New: 82.5m, Total: 412.5m),
            new StatementRules.TypeRow("tiles", "Tiles", null, 1, Earlier: 0, New: 1100, Total: 1100),
        ]);
        document.HiddenTypes.ShouldBe(1);
        document.HiddenTotal.ShouldBe(3300m);
        document.EarlierTotal.ShouldBe(3630m);
        document.NewTotal.ShouldBe(1182.5m);
        document.Total.ShouldBe(4812.5m);
        document.Count.ShouldBe(4);

        StatementRules.Summarise(StatementMode.Total, lines).Types.ShouldBeEmpty();
    }

    [Fact]
    public void Payment_position_is_what_remains_or_what_was_paid_ahead()
    {
        new StatementRules.PaymentPosition(Received: 400, Agreed: 1000).ShouldSatisfyAllConditions(
            p => p.Remaining.ShouldBe(600m), p => p.PaidAhead.ShouldBe(0m));
        new StatementRules.PaymentPosition(Received: 1200, Agreed: 1000).ShouldSatisfyAllConditions(
            p => p.Remaining.ShouldBe(0m), p => p.PaidAhead.ShouldBe(200m));
    }

    private static StatementRules.Choice Choice(
        StatementMode mode,
        bool includeEarlier = false,
        long[]? excluded = null,
        string[]? unlisted = null)
        => new(mode, includeEarlier, (excluded ?? []).ToHashSet(), (unlisted ?? []).ToHashSet());
}
