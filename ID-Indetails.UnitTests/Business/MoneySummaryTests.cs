using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MoneySummaryTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    private static ProjectBilling.Invoice Issued(long id, decimal total, decimal paid, DateOnly? dueOn)
        => new(id, 1, 1, IsIssued: true, IsVoided: false, dueOn, total, paid);

    [Fact]
    public void Period_revenue_weights_each_month_and_cost_counts_on_its_day()
    {
        var byMonth = new Dictionary<DateOnly, decimal> { [new(2026, 8, 1)] = 1000m, [new(2026, 9, 1)] = 600m };
        var shares = Recognition.Months(new DateOnly(2026, 8, 17), new DateOnly(2026, 9, 29), Today);

        // August 17-31 is 15 of 31 days; September is covered up to today.
        PeriodMoney.Revenue(byMonth, shares).ShouldBe(1000m * 15 / 31 + 600m, 0.01m);

        var costs = PeriodMoney.Costs(
            [
                new Recognition.DatedCost(CostSource.Labour, new(2026, 8, 16), 50m),
                new Recognition.DatedCost(CostSource.Labour, new(2026, 8, 17), 70m),
                new Recognition.DatedCost(CostSource.Materials, new(2026, 9, 29), 30m),
            ],
            new DateOnly(2026, 8, 17),
            Today);
        costs.ShouldBe(new PeriodMoney.CostTotals(70m, 0m, 30m, 0m));
        costs.Total.ShouldBe(100m);

        PeriodMoney.Margin(25m, 100m).ShouldBe(0.25m);
        PeriodMoney.Margin(-5m, 0m).ShouldBeNull();
    }

    [Fact]
    public void Unconverted_costs_count_in_the_period_of_their_day()
    {
        var unconverted = PeriodMoney.Unconverted(
            [
                new ProjectFinance.DatedUnconverted(CostSource.Expenses, "LBP", 100_000m, new(2026, 8, 16)),
                new ProjectFinance.DatedUnconverted(CostSource.Expenses, "LBP", 200_000m, new(2026, 8, 17)),
                new ProjectFinance.DatedUnconverted(CostSource.Expenses, "LBP", 300_000m, new(2026, 9, 29)),
                new ProjectFinance.DatedUnconverted(CostSource.Materials, "EUR", 40m, new(2026, 9, 1)),
            ],
            new DateOnly(2026, 8, 17),
            Today);

        unconverted.ShouldBe([
            new ProjectFinance.UnconvertedCost(CostSource.Expenses, "LBP", 500_000m),
            new ProjectFinance.UnconvertedCost(CostSource.Materials, "EUR", 40m),
        ]);
    }

    [Fact]
    public void The_billing_position_keeps_drafts_apart_and_on_account_payments_off_outstanding()
    {
        var invoices = new[]
        {
            Issued(1, 1000m, 400m, Today.AddDays(-10)),
            Issued(2, 500m, 500m, Today.AddDays(-40)),
            Issued(3, 300m, 0m, Today.AddDays(5)),
            new ProjectBilling.Invoice(4, 1, 1, IsIssued: false, IsVoided: false, null, 250m, 0m),
            new ProjectBilling.Invoice(5, 1, 1, IsIssued: true, IsVoided: true, Today.AddDays(-90), 800m, 0m),
        };
        var payments = new[]
        {
            new ProjectBilling.Payment(1, 1, 1, Today, 400m, PaymentMethod.Bank),
            new ProjectBilling.Payment(1, 1, 2, Today, 500m, PaymentMethod.Cash),
            new ProjectBilling.Payment(1, 1, null, Today, 200m, PaymentMethod.Cash),
        };

        var position = ProjectBilling.Summarise(3000m, invoices, payments, Today);

        position.Invoiced.ShouldBe(1800m);
        position.IssuedCount.ShouldBe(3);
        position.Received.ShouldBe(1100m);
        position.OnAccount.ShouldBe(200m);
        position.Outstanding.ShouldBe(900m);
        position.Overdue.ShouldBe(600m);
        position.OverdueCount.ShouldBe(1);
        position.OldestOverdueDue.ShouldBe(Today.AddDays(-10));
        position.NotYetInvoiced.ShouldBe(1200m);
        position.Drafts.ShouldBe(250m);
        position.PaymentCount.ShouldBe(3);
        position.Owed.ShouldBe(1900m);
        position.PaidAhead.ShouldBe(0m);
    }

    [Fact]
    public void Aging_lists_every_bucket_of_open_invoices()
    {
        var aging = ProjectBilling.Aging(
            [Issued(1, 100m, 0m, Today.AddDays(3)), Issued(2, 200m, 50m, Today.AddDays(-1)), Issued(3, 300m, 0m, Today.AddDays(-95)), Issued(4, 50m, 50m, Today.AddDays(-5))],
            Today);

        aging.Select(a => (a.FromDays, a.ToDays)).ShouldBe([(0, (int?)0), (1, 30), (31, 60), (61, 90), (91, null)]);
        aging.Select(a => a.Amount).ShouldBe([100m, 150m, 0m, 0m, 300m]);
        aging.Select(a => a.Count).ShouldBe([1, 1, 0, 0, 1]);
    }

    [Fact]
    public void Money_follow_ups_list_overdue_invoices_then_finished_owed_then_payment_warnings_six_at_most()
    {
        static MoneyAttention.Project Project(
            long id, ProjectStatus status, decimal revenue = 1000m, decimal cost = 0m, decimal outstanding = 0m, decimal overdue = 0m, bool client = true)
            => new(id, status, client, revenue, cost, outstanding, overdue);

        var projects = new[]
        {
            Project(1, ProjectStatus.Active, cost: 860m),
            Project(2, ProjectStatus.Active, cost: 850m),
            Project(3, ProjectStatus.Active, revenue: 0m),
            Project(4, ProjectStatus.Active, revenue: 0m, client: false),
            Project(5, ProjectStatus.Completed, outstanding: 300m),
            Project(6, ProjectStatus.Completed, outstanding: 500m, overdue: 500m),
            Project(7, ProjectStatus.Hold, revenue: 0m),
            Project(8, ProjectStatus.Archived, outstanding: 200m),
        };
        var invoices = new[]
        {
            new ProjectBilling.Invoice(10, 6, 1, IsIssued: true, IsVoided: false, Today.AddDays(-5), 500m, 0m),
            new ProjectBilling.Invoice(11, 1, 1, IsIssued: true, IsVoided: false, Today.AddDays(-20), 400m, 100m),
            new ProjectBilling.Invoice(12, 1, 1, IsIssued: true, IsVoided: false, Today.AddDays(3), 400m, 0m),
        };

        var items = MoneyAttention.Of(projects, invoices, Today, warnOnCost: true);

        // 85% exactly isn't past it, internal and on-hold projects are never flagged, an overdue project isn't also
        // "owed", and an archived project still owed is flagged like a completed one.
        items.Select(i => (i.Kind, i.ProjectId)).ShouldBe([
            (MoneyAttentionKind.OverdueInvoice, 1L),
            (MoneyAttentionKind.OverdueInvoice, 6L),
            (MoneyAttentionKind.FinishedOwed, 5L),
            (MoneyAttentionKind.FinishedOwed, 8L),
            (MoneyAttentionKind.CostHigh, 1L),
            (MoneyAttentionKind.NoPayment, 3L),
        ]);
        items[0].DaysOverdue.ShouldBe(20);
        items[0].Amount.ShouldBe(300m);
        items[2].Amount.ShouldBe(300m);
        items[4].UsedPct.ShouldBe(86);
        items[4].Amount.ShouldBe(860m);
        items[4].Revenue.ShouldBe(1000m);

        // Execution gets no cost warning.
        MoneyAttention.Of(projects, invoices, Today, warnOnCost: false).ShouldNotContain(i => i.Kind == MoneyAttentionKind.CostHigh);
        MoneyAttention.WarnsOnCost(DomainCodes.Design).ShouldBeTrue();
        MoneyAttention.WarnsOnCost(DomainCodes.Execution).ShouldBeFalse();

        var many = Enumerable.Range(1, 8).Select(id => Project(id, ProjectStatus.Active, revenue: 0m)).ToList();
        MoneyAttention.Of(many, [], Today, warnOnCost: true).Count.ShouldBe(MoneyAttention.MaxItems);
    }
}
