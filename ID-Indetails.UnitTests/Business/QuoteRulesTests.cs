using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class QuoteRulesTests
{
    [Fact]
    public void A_quote_is_numbered_by_year()
        => QuoteRules.Number(2026, 7).ShouldBe("QU-2026-007");

    [Fact]
    public void A_quote_is_for_designs_payment_or_a_sites_agreed_amount_before_materials()
    {
        QuoteRules.NetOf(DomainCodes.Design, revenue: 10_000m, projectFee: null).ShouldBe(10_000m);
        QuoteRules.NetOf(DomainCodes.Execution, revenue: 12_500m, projectFee: 8000m).ShouldBe(8000m);
    }

    [Fact]
    public void Only_client_work_with_an_agreed_amount_is_quoted()
    {
        QuoteRules.CheckQuotable(BillingType.Client, 100m).ShouldBeNull();
        QuoteRules.CheckQuotable(BillingType.Client, 0m)!.Code.ShouldBe(ErrorCodes.QuoteNotPossible);
        QuoteRules.CheckQuotable(BillingType.Internal, 100m)!.Message.ShouldContain("Internal");
    }
}
