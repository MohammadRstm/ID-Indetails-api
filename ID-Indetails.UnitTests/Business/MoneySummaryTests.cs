using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MoneySummaryTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    private static BillingPosition.Invoice Issued(long id, decimal total, decimal paid, DateOnly? dueOn)
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
    public void The_billing_position_keeps_drafts_apart_and_on_account_payments_off_outstanding()
    {
        var invoices = new[]
        {
            Issued(1, 1000m, 400m, Today.AddDays(-10)),
            Issued(2, 500m, 500m, Today.AddDays(-40)),
            Issued(3, 300m, 0m, Today.AddDays(5)),
            new BillingPosition.Invoice(4, 1, 1, IsIssued: false, IsVoided: false, null, 250m, 0m),
            new BillingPosition.Invoice(5, 1, 1, IsIssued: true, IsVoided: true, Today.AddDays(-90), 800m, 0m),
        };
        var payments = new[]
        {
            new BillingPosition.Payment(1, 1, 1, Today, 400m, PaymentMethod.Bank),
            new BillingPosition.Payment(1, 1, 2, Today, 500m, PaymentMethod.Cash),
            new BillingPosition.Payment(1, 1, null, Today, 200m, PaymentMethod.Cash),
        };

        var position = BillingPosition.Of(3000m, invoices, payments, Today);

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
    }

    [Fact]
    public void Aging_lists_every_bucket_of_open_invoices()
    {
        var aging = BillingPosition.Aging(
            [Issued(1, 100m, 0m, Today.AddDays(3)), Issued(2, 200m, 50m, Today.AddDays(-1)), Issued(3, 300m, 0m, Today.AddDays(-95)), Issued(4, 50m, 50m, Today.AddDays(-5))],
            Today);

        aging.Select(a => (a.FromDays, a.ToDays)).ShouldBe([(0, (int?)0), (1, 30), (31, 60), (61, 90), (91, null)]);
        aging.Select(a => a.Amount).ShouldBe([100m, 150m, 0m, 0m, 300m]);
        aging.Select(a => a.Count).ShouldBe([1, 1, 0, 0, 1]);
    }
}
