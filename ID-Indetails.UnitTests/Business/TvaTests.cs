using ID_Indetails.api.Business.Billing;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class TvaTests
{
    [Fact]
    public void The_client_pays_the_net_amount_with_the_rate_on_top()
    {
        Tva.Gross(10_000m, 0.15m).ShouldBe(11_500m);
        Tva.Gross(333.33m, 0.11m).ShouldBe(370m);
        Tva.Gross(500m, 0m).ShouldBe(500m);
    }

    [Fact]
    public void A_payment_holds_its_share_of_tva_at_the_projects_rate()
    {
        Tva.ShareOf(1150m, 0.15m).ShouldBe(150m);
        Tva.ShareOf(600m, 0.2m).ShouldBe(100m);
        Tva.ShareOf(1000m, 0m).ShouldBe(0m);
        Tva.ShareOf(100m, 0.15m).ShouldBe(13.04m);
    }

    [Fact]
    public void A_new_project_starts_at_11_percent()
        => Tva.DefaultRate.ShouldBe(0.11m);

    [Fact]
    public void Tva_of_an_amount_is_the_rate_on_it()
    {
        Tva.Of(787_646.20m, 0.11m).ShouldBe(86_641.08m);
        Tva.Of(500m, 0m).ShouldBe(0m);
    }

    [Fact]
    public void The_net_amount_for_what_a_client_paid_is_the_amount_whose_gross_it_is()
    {
        Tva.NetFor(370m, 0.11m).ShouldBe(333.33m);
        Tva.NetFor(11_500m, 0.15m).ShouldBe(10_000m);
        Tva.NetFor(500m, 0m).ShouldBe(500m);
        Tva.NetFor(0m, 0.11m).ShouldBe(0m);
    }

    [Theory]
    [InlineData(0.11)]
    [InlineData(0.15)]
    [InlineData(0.2)]
    public void Every_gross_a_net_can_make_goes_back_to_a_net_that_makes_it(double rate)
    {
        var tva = (decimal)rate;
        for (var cents = 1; cents <= 20_000; cents++)
        {
            var gross = Tva.Gross(cents / 100m, tva);

            Tva.Gross(Tva.NetFor(gross, tva), tva).ShouldBe(gross);
        }
    }
}
