using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Enums;
using Shouldly;
using Expense = ID_Indetails.api.Business.Finance.Intelligence.ExpenseTrendRules.Expense;

namespace ID_Indetails.UnitTests.Business;

public class ExpenseTrendRulesTests
{
    /// <summary>The owner's thresholds (Q16): 1.5× a 6-month average and 1,000 USD; 25% above the last 3 purchases; the same day.</summary>
    private static readonly ExpenseTrendRules.Thresholds Owner =
        new(BaselineMonths: 6, HighRatio: 1.5m, HighUsd: 1_000m, UnitCostJumpPct: 25, UnitCostPurchases: 3, DuplicateDays: 0);

    private static DateOnly Month(int year, int month) => new(year, month, 1);

    /// <summary>A USD expense of a category.</summary>
    private static Expense Spent(long id, string category, decimal usd, DateOnly on)
        => new(id, 1, "Prints", category, "USD", usd, usd, on, null, null);

    /// <summary>An expense with no exchange rate on its day.</summary>
    private static Expense Unconverted(long id, string category, decimal amount, string currency, DateOnly on)
        => new(id, 1, "Taxi", category, currency, amount, null, on, null, null);

    /// <summary>10 of an item at a unit cost, in USD.</summary>
    private static Expense Bought(long id, string item, string? unit, decimal unitCostUsd, DateOnly on)
        => new(id, 1, item, "materials", "USD", unitCostUsd * 10, unitCostUsd * 10, on, unit, unitCostUsd);

    /// <summary>An expense of 120 USD for plotter paper on project 7, unless said.</summary>
    private static Expense Typed(long id, DateOnly on, long projectId = 7, string item = "Plotter paper", decimal amount = 120m, string currency = "USD")
        => new(id, projectId, item, "other", currency, amount, amount, on, null, null);

    private static ExpenseTrendRules.Month OneCategory(decimal usd, decimal? baseline)
        => new(Month(2026, 5), usd, [new ExpenseTrendRules.CategoryMonth("travel", usd, baseline)]);

    private static bool CategoryHigh(decimal usd, decimal? baseline) => ExpenseTrendRules.CategoryHighs([OneCategory(usd, baseline)], Owner).Any();

    [Fact]
    public void Each_month_has_every_categorys_usd_total_in_the_schemes_order()
    {
        Expense[] all =
        [
            Spent(1, "travel", 100m, new DateOnly(2026, 1, 5)),
            Spent(2, "travel", 50m, new DateOnly(2026, 1, 20)),
            Spent(3, "software", 30m, new DateOnly(2026, 1, 10)),
            Spent(4, "travel", 70m, new DateOnly(2026, 2, 3)),
        ];

        var months = ExpenseTrendRules.Months(all, ["software", "travel", "other"], [Month(2026, 1), Month(2026, 2)], Owner);

        months.Select(m => m.Start).ShouldBe([Month(2026, 1), Month(2026, 2)]);
        months[0].Total.ShouldBe(180m);
        months[0].Categories.Select(c => (c.Category, c.Usd)).ShouldBe([("software", 30m), ("travel", 150m), ("other", 0m)]);
        months[1].Total.ShouldBe(70m);
        months[1].Categories.Select(c => (c.Category, c.Usd)).ShouldBe([("software", 0m), ("travel", 70m), ("other", 0m)]);
        // no months of history before them: no averages
        months.SelectMany(m => m.Categories).ShouldAllBe(c => c.Baseline == null);
    }

    [Fact]
    public void Spending_with_no_exchange_rate_is_in_no_usd_figure()
    {
        var day = new DateOnly(2026, 4, 8);
        Expense[] all = [Spent(1, "travel", 100m, day), Unconverted(2, "travel", 4_500_000m, "LBP", day)];

        var month = ExpenseTrendRules.Months(all, ["travel"], [Month(2026, 4)], Owner).ShouldHaveSingleItem();

        month.Total.ShouldBe(100m);
        month.Categories.ShouldHaveSingleItem().Usd.ShouldBe(100m);
    }

    [Fact]
    public void A_months_total_is_the_rounded_sum_of_its_unrounded_categories()
    {
        var day = new DateOnly(2026, 4, 8);
        Expense[] all = [Spent(1, "travel", 10.005m, day), Spent(2, "software", 10.005m, day)];

        var month = ExpenseTrendRules.Months(all, ["software", "travel"], [Month(2026, 4)], Owner).ShouldHaveSingleItem();

        month.Categories.Select(c => c.Usd).ShouldBe([10.01m, 10.01m]);
        // 20.01, not the 20.02 of the rounded parts
        month.Total.ShouldBe(20.01m);
    }

    [Fact]
    public void The_baseline_averages_the_six_months_before_counting_empty_ones_as_zero_once_there_are_six()
    {
        // history starts in January, the month of the earliest expense, however late in it that was
        Expense[] all =
        [
            Spent(1, "travel", 600m, new DateOnly(2026, 1, 31)),
            Spent(2, "travel", 300m, new DateOnly(2026, 3, 2)),
            Spent(3, "travel", 200m, new DateOnly(2026, 7, 9)),
        ];

        var months = ExpenseTrendRules.Months(all, ["software", "travel"], [Month(2026, 6), Month(2026, 7), Month(2026, 8)], Owner);
        decimal? Baseline(int index, string category) => months[index].Categories.Single(c => c.Category == category).Baseline;

        // June has only five months before it (January to May)
        Baseline(0, "travel").ShouldBeNull();
        // July: January to June, (600 + 0 + 300 + 0 + 0 + 0) ÷ 6
        Baseline(1, "travel").ShouldBe(150m);
        // August: February to July, (0 + 300 + 0 + 0 + 0 + 200) ÷ 6, rounded to the cent
        Baseline(2, "travel").ShouldBe(83.33m);
        // nothing at all in the six months before: no average, rather than an average of 0
        Baseline(1, "software").ShouldBeNull();
        Baseline(2, "software").ShouldBeNull();
    }

    [Fact]
    public void A_categorys_month_is_unusual_at_exactly_one_and_a_half_times_its_average()
    {
        var anomaly = ExpenseTrendRules.CategoryHighs([OneCategory(1_500m, 1_000m)], Owner).ShouldHaveSingleItem();

        anomaly.ShouldSatisfyAllConditions(
            a => a.Kind.ShouldBe(ExpenseAnomalyKind.CategoryHigh),
            a => a.On.ShouldBe(Month(2026, 5)),
            a => a.Category.ShouldBe("travel"),
            a => a.ProjectId.ShouldBeNull(),
            a => a.Item.ShouldBeNull(),
            a => a.Usd.ShouldBe(1_500m),
            a => a.Baseline.ShouldBe(1_000m),
            a => a.Pct.ShouldBe(50),
            a => a.ExpenseIds.ShouldBeEmpty(),
            a => a.Weight.ShouldBe(500m),
            a => a.Fact.ShouldBe("Travel: 1,500 USD in May 2026, 1.5× its 6-month average of 1,000 USD."),
            a => a.Rule.ShouldBe(ExpenseTrendRules.CategoryHighRule(Owner)));

        CategoryHigh(1_490m, 1_000m).ShouldBeFalse();
    }

    [Fact]
    public void A_categorys_month_is_unusual_only_from_1000_usd()
    {
        // 1.67× its average of 600 either way
        CategoryHigh(1_000m, 600m).ShouldBeTrue();
        CategoryHigh(999.99m, 600m).ShouldBeFalse();
    }

    [Fact]
    public void A_category_with_no_average_is_never_unusual()
    {
        CategoryHigh(50_000m, null).ShouldBeFalse();
    }

    [Fact]
    public void A_unit_cost_exactly_25_percent_above_the_last_three_purchases_is_unusual()
    {
        Expense[] history =
        [
            Bought(1, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 2)),
            Bought(2, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 9)),
            Bought(3, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 16)),
        ];

        var jump = ExpenseTrendRules.UnitCostJumps([.. history, Bought(4, "Cement bag", "bag", 12.5m, new DateOnly(2026, 3, 23))],
            new DateOnly(2026, 3, 1), Owner).ShouldHaveSingleItem();

        jump.ShouldSatisfyAllConditions(
            a => a.Kind.ShouldBe(ExpenseAnomalyKind.UnitCostJump),
            a => a.On.ShouldBe(new DateOnly(2026, 3, 23)),
            a => a.Category.ShouldBe("materials"),
            a => a.ProjectId.ShouldBe(1L),
            a => a.Item.ShouldBe("Cement bag"),
            a => a.Usd.ShouldBe(125m),
            a => a.Baseline.ShouldBe(10m),
            a => a.Pct.ShouldBe(25),
            a => a.ExpenseIds.ShouldBe([4L]),
            a => a.Weight.ShouldBe(25m),
            a => a.Fact.ShouldBe("\"Cement bag\" at 12.5 USD a bag on 23 Mar 2026, 25% above the average of its last 3 purchases (10 USD)."),
            a => a.Rule.ShouldBe(ExpenseTrendRules.UnitCostRule(Owner)));

        // 24.99% above
        ExpenseTrendRules.UnitCostJumps([.. history, Bought(4, "Cement bag", "bag", 12.499m, new DateOnly(2026, 3, 23))],
            new DateOnly(2026, 3, 1), Owner).ShouldBeEmpty();
    }

    [Fact]
    public void A_unit_cost_is_judged_against_the_last_three_purchases_only()
    {
        // 100, 10, 10, 10: the average of the last three is 10, not the 32.5 of all four
        Expense[] all =
        [
            Bought(1, "Cement bag", "bag", 100m, new DateOnly(2026, 3, 2)),
            Bought(2, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 9)),
            Bought(3, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 16)),
            Bought(4, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 23)),
            Bought(5, "Cement bag", "bag", 12.5m, new DateOnly(2026, 3, 30)),
        ];

        var jump = ExpenseTrendRules.UnitCostJumps(all, new DateOnly(2026, 3, 1), Owner).ShouldHaveSingleItem();

        jump.ExpenseIds.ShouldBe([5L]);
        jump.Baseline.ShouldBe(10m);
    }

    [Fact]
    public void A_unit_cost_needs_three_earlier_purchases_of_the_same_item_and_unit()
    {
        var from = new DateOnly(2026, 3, 1);

        // two earlier purchases are not enough, however big the jump
        ExpenseTrendRules.UnitCostJumps(
        [
            Bought(1, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 2)),
            Bought(2, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 9)),
            Bought(3, "Cement bag", "bag", 50m, new DateOnly(2026, 3, 16)),
        ], from, Owner).ShouldBeEmpty();

        // names and units match trimmed, in any case and with single spaces; another unit is another item
        Expense[] all =
        [
            Bought(1, "Cement  bag", "Bag", 10m, new DateOnly(2026, 3, 2)),
            Bought(2, " cement bag ", "bag ", 10m, new DateOnly(2026, 3, 9)),
            Bought(3, "CEMENT BAG", "BAG", 10m, new DateOnly(2026, 3, 16)),
            Bought(4, "Cement bag", "kg", 50m, new DateOnly(2026, 3, 20)),
            Bought(5, "Cement bags", "bag", 50m, new DateOnly(2026, 3, 21)),
            Bought(6, "Cement bag", "bag", 15m, new DateOnly(2026, 3, 23)),
        ];

        ExpenseTrendRules.UnitCostJumps(all, from, Owner).ShouldHaveSingleItem().ExpenseIds.ShouldBe([6L]);
    }

    [Fact]
    public void Purchases_before_the_window_count_as_history_but_are_not_reported()
    {
        // three at 10 and a jump to 20, all in February; then 20 again in March, 50% above (10 + 10 + 20) ÷ 3
        Expense[] all =
        [
            Bought(1, "Rebar", "ton", 10m, new DateOnly(2026, 2, 2)),
            Bought(2, "Rebar", "ton", 10m, new DateOnly(2026, 2, 9)),
            Bought(3, "Rebar", "ton", 10m, new DateOnly(2026, 2, 16)),
            Bought(4, "Rebar", "ton", 20m, new DateOnly(2026, 2, 23)),
            Bought(5, "Rebar", "ton", 20m, new DateOnly(2026, 3, 4)),
        ];

        var jump = ExpenseTrendRules.UnitCostJumps(all, new DateOnly(2026, 3, 1), Owner).ShouldHaveSingleItem();

        jump.ExpenseIds.ShouldBe([5L]);
        jump.Baseline.ShouldBe(13.3333m);
        jump.Pct.ShouldBe(50);
    }

    [Fact]
    public void Expenses_with_no_unit_cost_are_not_purchases()
    {
        var day = new DateOnly(2026, 3, 9);
        Expense[] all =
        [
            Bought(1, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 2)),
            // a lump sum: no quantity × unit cost
            new(2, 1, "Cement bag", "materials", "USD", 500m, 500m, day, null, null),
            Bought(3, "Cement bag", "bag", 10m, new DateOnly(2026, 3, 16)),
            Bought(4, "Cement bag", "bag", 20m, new DateOnly(2026, 3, 23)),
        ];

        ExpenseTrendRules.UnitCostJumps(all, new DateOnly(2026, 3, 1), Owner).ShouldBeEmpty();
    }

    [Fact]
    public void The_same_item_amount_and_currency_twice_on_one_project_and_day_is_reported_once_with_both()
    {
        var day = new DateOnly(2026, 3, 4);

        var twice = ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day, item: "plotter  Paper")], day, Owner).ShouldHaveSingleItem();

        twice.ShouldSatisfyAllConditions(
            a => a.Kind.ShouldBe(ExpenseAnomalyKind.SameAmountTwice),
            a => a.On.ShouldBe(day),
            a => a.Category.ShouldBe("other"),
            a => a.ProjectId.ShouldBe(7L),
            a => a.Item.ShouldBe("Plotter paper"),
            a => a.Usd.ShouldBe(120m),
            a => a.Baseline.ShouldBeNull(),
            a => a.Pct.ShouldBeNull(),
            a => a.ExpenseIds.ShouldBe([1L, 2L]),
            a => a.Weight.ShouldBe(2m),
            a => a.Fact.ShouldBe("2 expenses of 120 USD for \"Plotter paper\" on 4 Mar 2026."),
            a => a.Rule.ShouldBe(ExpenseTrendRules.DuplicateRule(Owner)));
    }

    [Fact]
    public void Expenses_a_day_apart_on_other_projects_or_of_other_amounts_are_not_the_same()
    {
        var day = new DateOnly(2026, 3, 4);
        var from = new DateOnly(2026, 3, 1);

        ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day.AddDays(1))], from, Owner).ShouldBeEmpty();
        ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day, projectId: 8)], from, Owner).ShouldBeEmpty();
        ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day, amount: 121m)], from, Owner).ShouldBeEmpty();
        ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day, currency: "EUR")], from, Owner).ShouldBeEmpty();
    }

    [Fact]
    public void With_three_days_allowed_three_days_apart_is_the_same_and_four_is_not()
    {
        var spread = Owner with { DuplicateDays = 3 };
        var day = new DateOnly(2026, 3, 2);
        var from = new DateOnly(2026, 3, 1);

        var twice = ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day.AddDays(3))], from, spread).ShouldHaveSingleItem();
        twice.On.ShouldBe(day.AddDays(3));
        twice.ExpenseIds.ShouldBe([1L, 2L]);
        twice.Fact.ShouldBe("2 expenses of 120 USD for \"Plotter paper\" from 2 Mar 2026 to 5 Mar 2026.");
        twice.Rule.ShouldBe(ExpenseTrendRules.DuplicateRule(spread));

        ExpenseTrendRules.Duplicates([Typed(1, day), Typed(2, day.AddDays(4))], from, spread).ShouldBeEmpty();
    }

    [Fact]
    public void Repeats_that_ended_before_the_window_are_not_reported()
    {
        var from = new DateOnly(2026, 3, 1);
        var lastOfFebruary = new DateOnly(2026, 2, 28);

        ExpenseTrendRules.Duplicates([Typed(1, lastOfFebruary), Typed(2, lastOfFebruary)], from, Owner).ShouldBeEmpty();

        // one that runs into the window is
        ExpenseTrendRules.Duplicates([Typed(1, lastOfFebruary), Typed(2, from)], from, Owner with { DuplicateDays = 3 })
            .ShouldHaveSingleItem().ExpenseIds.ShouldBe([1L, 2L]);
    }

    [Fact]
    public void Anomalies_come_newest_first_then_by_kind_then_by_weight()
    {
        var march1 = new DateOnly(2026, 3, 1);
        var march20 = new DateOnly(2026, 3, 20);
        ExpenseTrendRules.Month[] months = [new(Month(2026, 3), 3_000m, [new ExpenseTrendRules.CategoryMonth("travel", 3_000m, 1_000m)])];
        Expense[] all =
        [
            Typed(1, march1, item: "Taxi", amount: 50m),
            Typed(2, march1, item: "Taxi", amount: 50m),
            Typed(3, march20, item: "Prints", amount: 30m),
            Typed(4, march20, item: "Prints", amount: 30m),
            Typed(5, march20, item: "Paper", amount: 10m),
            Typed(6, march20, item: "Paper", amount: 10m),
            Typed(7, march20, item: "Paper", amount: 10m),
            Bought(8, "Rebar", "ton", 600m, new DateOnly(2026, 1, 5)),
            Bought(9, "Rebar", "ton", 600m, new DateOnly(2026, 1, 12)),
            Bought(10, "Rebar", "ton", 600m, new DateOnly(2026, 1, 19)),
            Bought(11, "Rebar", "ton", 900m, march20),
        ];

        var anomalies = ExpenseTrendRules.Anomalies(months, all, march1, Owner);

        anomalies.Select(a => (a.On, a.Kind, First: a.ExpenseIds.FirstOrDefault())).ShouldBe([
            (march20, ExpenseAnomalyKind.UnitCostJump, 11L),
            // three of the same outweigh two
            (march20, ExpenseAnomalyKind.SameAmountTwice, 5L),
            (march20, ExpenseAnomalyKind.SameAmountTwice, 3L),
            (march1, ExpenseAnomalyKind.CategoryHigh, 0L),
            (march1, ExpenseAnomalyKind.SameAmountTwice, 1L)]);
    }
}
