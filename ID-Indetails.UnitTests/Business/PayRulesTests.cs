using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PayRulesTests
{
    [Fact]
    public void Hourly_pay_is_its_own_cost_rate()
        => PayRules.HourlyCostRate(PayBasis.Hourly, 18.50m).ShouldBe(18.50m);

    [Fact]
    public void Monthly_salary_is_divided_by_average_monthly_hours()
        => PayRules.HourlyCostRate(PayBasis.Monthly, 3000m).ShouldBe(17.3080m); // 3000 / 173.33

    [Fact]
    public void Monthly_rate_is_rounded_to_four_decimals()
        => decimal.Round(PayRules.HourlyCostRate(PayBasis.Monthly, 1000m), 4).ShouldBe(PayRules.HourlyCostRate(PayBasis.Monthly, 1000m));
}
