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
}
