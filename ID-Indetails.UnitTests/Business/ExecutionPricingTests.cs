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
        var price = ExecutionPricing.PriceOf(787_646.20m, supervisionRate: 0.08m, tvaRate: 0.11m);

        price.ShouldSatisfyAllConditions(
            p => p.Supervision.ShouldBe(63_011.70m),
            p => p.Net.ShouldBe(850_657.90m),
            p => p.Tva.ShouldBe(86_641.08m),
            p => p.Gross.ShouldBe(937_298.98m));
    }

    [Fact]
    public void Tva_is_on_the_work_never_on_supervision()
    {
        var price = ExecutionPricing.PriceOf(1000m, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.Supervision.ShouldBe(100m);
        price.Tva.ShouldBe(110m);
        price.Gross.ShouldBe(1210m);
        Tva.Gross(price.Net, 0.11m).ShouldNotBe(price.Gross);
    }

    [Fact]
    public void A_site_with_no_price_yet_is_all_zero()
    {
        var price = ExecutionPricing.PriceOf(0m, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.ShouldBe(new ExecutionPricing.Price(0m, 0m, 0m, 0m));
    }

    [Fact]
    public void No_supervision_and_no_tva_leave_the_work_price()
    {
        var price = ExecutionPricing.PriceOf(5000m, supervisionRate: 0m, tvaRate: 0m);

        price.ShouldBe(new ExecutionPricing.Price(0m, 5000m, 0m, 5000m));
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
        var work = ExecutionPricing.WorkFor(174_342.11m, supervisionRate: 0.08m, tvaRate: 0m);

        work.ShouldBe(161_427.88m);
        ExecutionPricing.PriceOf(work, 0.08m, 0m).Gross.ShouldBe(174_342.11m);
        ExecutionPricing.WorkFor(1_210m, 0.10m, 0.11m).ShouldBe(1_000m);
        ExecutionPricing.WorkFor(500m, 0m, 0m).ShouldBe(500m);
        ExecutionPricing.WorkFor(0m, 0.10m, 0.11m).ShouldBe(0m);
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
            var gross = ExecutionPricing.PriceOf(cents / 100m, s, t).Gross;

            ExecutionPricing.PriceOf(ExecutionPricing.WorkFor(gross, s, t), s, t).Gross.ShouldBe(gross);
        }
    }
}
