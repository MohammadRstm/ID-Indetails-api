using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PaymentRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Usd_value_is_amount_over_rate_to_the_cent()
    {
        PaymentRules.ToUsd(895_000m, 89_500m).ShouldBe(10m);
        PaymentRules.ToUsd(100_000m, 89_500m).ShouldBe(1.12m);
        PaymentRules.ToUsd(250.75m, 1m).ShouldBe(250.75m);
        PaymentRules.CheckUsd(PaymentRules.ToUsd(100m, 89_500m))!.Code.ShouldBe(ErrorCodes.PaymentTooSmall);
    }

    [Fact]
    public void Payments_are_never_dated_in_the_future()
    {
        PaymentRules.CheckReceivedOn(Today, Today).ShouldBeNull();
        PaymentRules.CheckReceivedOn(Today.AddDays(1), Today)!.Code.ShouldBe(ErrorCodes.ReceivedDateInFuture);
    }

    [Fact]
    public void A_payment_fits_an_issued_live_invoice_of_the_same_project_and_its_balance()
    {
        PaymentRules.CheckInvoice(1, 1, isIssued: true, isVoided: false, balance: 500m, amountUsd: 500m).ShouldBeNull();
        PaymentRules.CheckInvoice(1, 2, true, false, 500m, 10m)!.Field.ShouldBe("invoiceId");
        PaymentRules.CheckInvoice(1, 1, isIssued: false, isVoided: false, 500m, 10m)!.Code.ShouldBe(ErrorCodes.InvoiceNotIssued);
        PaymentRules.CheckInvoice(1, 1, isIssued: true, isVoided: true, 500m, 10m)!.Code.ShouldBe(ErrorCodes.InvoiceVoided);
        PaymentRules.CheckInvoice(1, 1, true, false, balance: 500m, amountUsd: 500.01m)!.Code.ShouldBe(ErrorCodes.PaymentExceedsBalance);
    }

    [Fact]
    public void One_live_initial_payment_and_only_live_on_account_payments_are_applied()
    {
        PaymentRules.CheckInitial(isInitial: true, DomainCodes.Execution, projectHasLiveInitial: false).ShouldBeNull();
        PaymentRules.CheckInitial(isInitial: false, DomainCodes.Execution, projectHasLiveInitial: true).ShouldBeNull();
        PaymentRules.CheckInitial(isInitial: true, DomainCodes.Execution, projectHasLiveInitial: true)!.Code.ShouldBe(ErrorCodes.InitialPaymentExists);
        PaymentRules.CheckInitial(isInitial: true, DomainCodes.Design, projectHasLiveInitial: false)!.Field.ShouldBe("isInitial");
        PaymentRules.CheckInitial(isInitial: false, DomainCodes.Design, projectHasLiveInitial: false).ShouldBeNull();

        PaymentRules.CheckApply(isVoided: false, invoiceId: null).ShouldBeNull();
        PaymentRules.CheckApply(isVoided: false, invoiceId: 3)!.Code.ShouldBe(ErrorCodes.PaymentAlreadyApplied);
        PaymentRules.CheckApply(isVoided: true, invoiceId: null)!.Code.ShouldBe(ErrorCodes.PaymentAlreadyVoided);
    }

    [Fact]
    public void Project_billing_counts_on_account_payments_as_received_but_not_against_invoices()
    {
        ProjectBilling.Invoice Issued(long id, decimal total, decimal paid) => new(id, 1, 1, IsIssued: true, IsVoided: false, Today, total, paid);

        var summary = ProjectBilling.Summarise(
            10_000m,
            [Issued(1, 4000m, 1000m), Issued(2, 2000m, 2000m)],
            [
                new ProjectBilling.Payment(1, 1, 1, Today, 1000m, PaymentMethod.Bank),
                new ProjectBilling.Payment(1, 1, 2, Today, 2000m, PaymentMethod.Bank),
                new ProjectBilling.Payment(1, 1, null, Today, 2000m, PaymentMethod.Cash),
            ],
            Today);

        summary.Invoiced.ShouldBe(6000m);
        summary.Outstanding.ShouldBe(3000m);
        summary.Received.ShouldBe(5000m);
        summary.OnAccount.ShouldBe(2000m);
        summary.NotYetInvoiced.ShouldBe(4000m);
        summary.Owed.ShouldBe(5000m);
        summary.PaidAhead.ShouldBe(0m);

        ProjectBilling.Summarise(1000m, [], [new ProjectBilling.Payment(1, 1, null, Today, 1500m, PaymentMethod.Cash)], Today)
            .PaidAhead.ShouldBe(500m);
    }
}
