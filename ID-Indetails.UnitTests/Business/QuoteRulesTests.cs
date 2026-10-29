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
    public void Only_design_client_work_with_an_agreed_amount_is_quoted()
    {
        QuoteRules.CheckQuotable(DomainCodes.Design, BillingType.Client, 100m).ShouldBeNull();
        QuoteRules.CheckQuotable(DomainCodes.Design, BillingType.Client, 0m)!.Code.ShouldBe(ErrorCodes.QuoteNotPossible);
        QuoteRules.CheckQuotable(DomainCodes.Design, BillingType.Internal, 100m)!.Message.ShouldContain("Internal");
    }

    [Fact]
    public void Execution_has_no_quotes_whatever_its_amount()
    {
        QuoteRules.CheckQuotable(DomainCodes.Execution, BillingType.Client, 100m)!.Code.ShouldBe(ErrorCodes.QuoteNotForExecution);
        QuoteRules.CheckQuotable(DomainCodes.Execution, BillingType.Client, 0m)!.Code.ShouldBe(ErrorCodes.QuoteNotForExecution);
    }
}
