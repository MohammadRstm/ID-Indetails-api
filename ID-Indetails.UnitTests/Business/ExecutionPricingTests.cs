using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ExecutionPricingTests
{
    [Fact]
    public void Supervision_is_a_share_of_the_work_and_the_agreed_amount_is_work_plus_supervision()
    {
        // a real site: 787,646.20 of work at 8% supervision and 11% TVA
        var price = ExecutionPricing.PriceOf(787_646.20m, permits: 0m, supervisionRate: 0.08m, tvaRate: 0.11m);

        price.ShouldSatisfyAllConditions(
            p => p.Supervision.ShouldBe(63_011.70m),
            p => p.Net.ShouldBe(850_657.90m),
            p => p.Tva.ShouldBe(86_641.08m),
            p => p.Gross.ShouldBe(937_298.98m));
    }

    [Fact]
    public void Tva_is_on_the_work_never_on_supervision()
    {
        var price = ExecutionPricing.PriceOf(1000m, permits: 0m, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.Supervision.ShouldBe(100m);
        price.Tva.ShouldBe(110m);
        price.Gross.ShouldBe(1210m);
        Tva.Gross(price.Net, 0.11m).ShouldNotBe(price.Gross);
    }

    [Fact]
    public void Permits_are_charged_as_they_are_with_no_supervision_and_no_tva()
    {
        // the Mila 2 permits on 10,000 of work, 10% supervision and 11% TVA
        var price = ExecutionPricing.PriceOf(10_000m, permits: 64_814.81m, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.ShouldSatisfyAllConditions(
            p => p.Supervision.ShouldBe(1000m),
            p => p.Tva.ShouldBe(1100m),
            p => p.Net.ShouldBe(75_814.81m),
            p => p.Gross.ShouldBe(76_914.81m));
        ExecutionPricing.PriceOf(0m, 500m, 0.10m, 0.11m).ShouldBe(new ExecutionPricing.Price(0m, 500m, 0m, 500m));
    }

    [Fact]
    public void The_work_price_for_what_a_client_paid_leaves_the_permits_as_they_are()
    {
        // 1,210 for the work with its supervision and TVA, and 300 of permits
        ExecutionPricing.WorkFor(1_510m, permits: 300m, supervisionRate: 0.10m, tvaRate: 0.11m).ShouldBe(1_000m);

        // any client total a work price makes goes back to it, permits and all
        var gross = ExecutionPricing.PriceOf(1_404.96m, 300m, 0.10m, 0.11m).Gross;
        ExecutionPricing.PriceOf(ExecutionPricing.WorkFor(gross, 300m, 0.10m, 0.11m), 300m, 0.10m, 0.11m).Gross.ShouldBe(gross);
    }

    [Fact]
    public void A_site_with_no_price_yet_is_all_zero()
    {
        var price = ExecutionPricing.PriceOf(0m, permits: 0m, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.ShouldBe(new ExecutionPricing.Price(0m, 0m, 0m, 0m));
    }

    [Fact]
    public void No_supervision_and_no_tva_leave_the_work_price()
    {
        var price = ExecutionPricing.PriceOf(5000m, permits: 0m, supervisionRate: 0m, tvaRate: 0m);

        price.ShouldBe(new ExecutionPricing.Price(0m, 5000m, 0m, 5000m));
    }

    [Fact]
    public void An_amount_without_tva_is_paid_as_it_is_and_the_rest_carries_it()
    {
        // 11,000 agreed, 1,000 of it a VO without TVA
        Tva.Gross(11_000m, 0.11m, untaxed: 1000m).ShouldBe(12_100m);
        Tva.Gross(11_000m, 0.11m, untaxed: 0m).ShouldBe(Tva.Gross(11_000m, 0.11m));
        Tva.NetFor(12_100m, 0.11m, untaxed: 1000m).ShouldBe(11_000m);
    }

    [Fact]
    public void A_payment_holds_its_share_of_the_projects_tva()
    {
        // the client pays 1,210 in all, 110 of it TVA: a payment of 605 holds half of it
        ExecutionPricing.TvaShareOf(605m, projectTva: 110m, projectGross: 1210m, tvaRate: 0.11m).ShouldBe(55m);
        ExecutionPricing.TvaShareOf(1210m, 110m, 1210m, 0.11m).ShouldBe(110m);
    }

    [Fact]
    public void A_payment_before_a_price_is_agreed_holds_the_plain_tva_share()
        => ExecutionPricing.TvaShareOf(1110m, projectTva: 0m, projectGross: 0m, tvaRate: 0.11m).ShouldBe(110m);

    [Fact]
    public void A_new_site_starts_at_10_percent_supervision()
        => ExecutionPricing.DefaultSupervisionRate.ShouldBe(0.10m);

    [Fact]
    public void The_work_price_for_what_a_client_paid_makes_a_client_total_of_exactly_that()
    {
        // a real site that paid ahead: 174,342.11 received, 8% supervision and no TVA
        var work = ExecutionPricing.WorkFor(174_342.11m, permits: 0m, supervisionRate: 0.08m, tvaRate: 0m);

        work.ShouldBe(161_427.88m);
        ExecutionPricing.PriceOf(work, 0m, 0.08m, 0m).Gross.ShouldBe(174_342.11m);
        ExecutionPricing.WorkFor(1_210m, 0m, 0.10m, 0.11m).ShouldBe(1_000m);
        ExecutionPricing.WorkFor(500m, 0m, 0m, 0m).ShouldBe(500m);
        ExecutionPricing.WorkFor(0m, 0m, 0.10m, 0.11m).ShouldBe(0m);
    }

    [Theory]
    [InlineData(0.08, 0)]
    [InlineData(0.10, 0.11)]
    [InlineData(0.07, 0.15)]
    public void Every_client_total_a_work_price_can_make_goes_back_to_a_work_price_that_makes_it(double supervision, double tva)
    {
        var (s, t) = ((decimal)supervision, (decimal)tva);
        for (var cents = 1; cents <= 20_000; cents++)
        {
            var gross = ExecutionPricing.PriceOf(cents / 100m, 0m, s, t).Gross;

            ExecutionPricing.PriceOf(ExecutionPricing.WorkFor(gross, 0m, s, t), 0m, s, t).Gross.ShouldBe(gross);
        }
    }
}
