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
        var price = ExecutionPricing.PriceOf(787_646.20m, materials: 0, supervisionRate: 0.08m, tvaRate: 0.11m);

        price.ShouldSatisfyAllConditions(
            p => p.Supervision.ShouldBe(63_011.70m),
            p => p.Net.ShouldBe(850_657.90m),
            p => p.Tva.ShouldBe(86_641.08m),
            p => p.Gross.ShouldBe(937_298.98m));
    }

    [Fact]
    public void Tva_is_on_the_work_never_on_supervision()
    {
        var price = ExecutionPricing.PriceOf(1000m, materials: 0, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.Supervision.ShouldBe(100m);
        price.Tva.ShouldBe(110m);
        price.Gross.ShouldBe(1210m);
        Tva.Gross(price.Net, 0.11m).ShouldNotBe(price.Gross);
    }

    [Fact]
    public void A_site_with_no_price_yet_is_all_zero()
    {
        var price = ExecutionPricing.PriceOf(0m, materials: 0, supervisionRate: 0.10m, tvaRate: 0.11m);

        price.ShouldBe(new ExecutionPricing.Price(0m, 0m, 0m, 0m));
    }

    [Fact]
    public void Materials_billed_to_the_client_are_part_of_the_agreed_amount_and_taxed_like_the_work()
    {
        var price = ExecutionPricing.PriceOf(1000m, materials: 270m, supervisionRate: 0.10m, tvaRate: 0.10m);

        price.Supervision.ShouldBe(100m);
        price.Net.ShouldBe(1370m);
        price.Tva.ShouldBe(127m);
        price.Gross.ShouldBe(1497m);
    }

    [Fact]
    public void No_supervision_and_no_tva_leave_the_work_price()
    {
        var price = ExecutionPricing.PriceOf(5000m, materials: 0, supervisionRate: 0m, tvaRate: 0m);

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
}
