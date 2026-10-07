using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Billing;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class InvoiceRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Theory]
    [InlineData(true, true, 100, 100, InvoiceStatus.Void)]
    [InlineData(false, false, 100, 0, InvoiceStatus.Draft)]
    [InlineData(false, true, 100, 0, InvoiceStatus.Sent)]
    [InlineData(false, true, 100, 0.01, InvoiceStatus.Partial)]
    [InlineData(false, true, 100, 99.99, InvoiceStatus.Partial)]
    [InlineData(false, true, 100, 100, InvoiceStatus.Paid)]
    public void Status_comes_from_issue_void_and_exact_payments(bool voided, bool issued, decimal total, decimal paid, InvoiceStatus expected)
        => InvoiceRules.Status(voided, issued, total, paid).ShouldBe(expected);

    [Fact]
    public void Balance_never_goes_below_zero()
    {
        InvoiceRules.Balance(100m, 40m).ShouldBe(60m);
        InvoiceRules.Balance(100m, 100m).ShouldBe(0m);
    }

    [Fact]
    public void Only_unpaid_issued_invoices_past_their_due_date_are_overdue()
    {
        InvoiceRules.IsOverdue(InvoiceStatus.Sent, Today.AddDays(-1), Today).ShouldBeTrue();
        InvoiceRules.IsOverdue(InvoiceStatus.Partial, Today.AddDays(-1), Today).ShouldBeTrue();
        InvoiceRules.IsOverdue(InvoiceStatus.Sent, Today, Today).ShouldBeFalse();
        InvoiceRules.IsOverdue(InvoiceStatus.Paid, Today.AddDays(-1), Today).ShouldBeFalse();
        InvoiceRules.IsOverdue(InvoiceStatus.Draft, null, Today).ShouldBeFalse();
    }

    [Fact]
    public void Numbers_are_prefix_year_and_a_three_digit_sequence()
    {
        InvoiceRules.Number("HL", 2026, 7).ShouldBe("HL-2026-007");
        InvoiceRules.Number("EX", 2027, 1234).ShouldBe("EX-2027-1234");
    }

    [Fact]
    public void Lifecycle_checks()
    {
        InvoiceRules.CheckBillable(BillingType.Client, 5).ShouldBeNull();
        InvoiceRules.CheckBillable(BillingType.Internal, null)!.Code.ShouldBe(ErrorCodes.ProjectNotBillable);
        InvoiceRules.CheckBillable(BillingType.Client, null)!.Code.ShouldBe(ErrorCodes.ProjectNotBillable);

        InvoiceRules.CheckDraft(isIssued: false, isVoided: false).ShouldBeNull();
        InvoiceRules.CheckDraft(isIssued: true, isVoided: false)!.Code.ShouldBe(ErrorCodes.InvoiceNotDraft);

        InvoiceRules.CheckIssueDate(Today, Today).ShouldBeNull();
        InvoiceRules.CheckIssueDate(Today.AddDays(-30), Today).ShouldBeNull();
        InvoiceRules.CheckIssueDate(Today.AddDays(1), Today)!.Code.ShouldBe(ErrorCodes.IssueDateInFuture);

        // reminders: the first some days after the due date, the next some days after the last, up to the most
        var due = new DateOnly(2026, 9, 1);
        InvoiceRules.NextReminderOn(due, 0, null, 1, 7, 3).ShouldBe(new DateOnly(2026, 9, 2));
        InvoiceRules.NextReminderOn(due, 1, new DateOnly(2026, 9, 5), 1, 7, 3).ShouldBe(new DateOnly(2026, 9, 12));
        InvoiceRules.NextReminderOn(due, 3, new DateOnly(2026, 9, 20), 1, 7, 3).ShouldBeNull();

        // a scheduled invoice goes out today or later, never on a day already gone
        InvoiceRules.CheckSendOn(Today, Today).ShouldBeNull();
        InvoiceRules.CheckSendOn(Today.AddDays(60), Today).ShouldBeNull();
        InvoiceRules.CheckSendOn(Today.AddDays(-1), Today)!.Code.ShouldBe(ErrorCodes.SendDateInPast);

        InvoiceRules.CheckLive(isIssued: false, isVoided: false)!.Code.ShouldBe(ErrorCodes.InvoiceNotIssued);
        InvoiceRules.CheckLive(isIssued: true, isVoided: true)!.Code.ShouldBe(ErrorCodes.InvoiceVoided);

        InvoiceRules.CheckVoid(isIssued: true, isVoided: false, hasLivePayments: false).ShouldBeNull();
        InvoiceRules.CheckVoid(isIssued: true, isVoided: false, hasLivePayments: true)!.Code.ShouldBe(ErrorCodes.InvoiceHasPayments);
        InvoiceRules.CheckVoid(isIssued: false, isVoided: false, hasLivePayments: false)!.Code.ShouldBe(ErrorCodes.InvoiceNotIssued);
    }

    [Fact]
    public void An_invoice_needs_valid_lines()
    {
        var validator = new SaveInvoiceRequestValidator();

        validator.Validate(new SaveInvoiceRequest { Lines = [new() { Description = "Design fee", Amount = 1500.50m }] }).IsValid.ShouldBeTrue();
        validator.Validate(new SaveInvoiceRequest { Lines = [] }).IsValid.ShouldBeFalse();
        validator.Validate(new SaveInvoiceRequest { Lines = [new() { Description = " ", Amount = 0 }] }).Errors.Count.ShouldBe(2);
        validator.Validate(new SaveInvoiceRequest { Lines = [new() { Description = "Fee", Amount = 10m }], TermsDays = 400 }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_invoices_balance_compares_payments_in_usd_and_stays_in_its_own_currency()
    {
        // 89,500,000 LBP at 89,500 a dollar is $1,000; payments are summed in USD
        InvoiceRules.BalanceIn(89_500_000m, 1000m, 1000m).ShouldBe(0m);
        InvoiceRules.BalanceIn(89_500_000m, 1000m, 400m).ShouldBe(53_700_000m);
        InvoiceRules.Status(isVoided: false, isIssued: true, total: 1000m, paid: 1000m).ShouldBe(InvoiceStatus.Paid);
        // a dollar invoice is as before
        InvoiceRules.BalanceIn(1200m, 1200m, 200m).ShouldBe(1000m);
        InvoiceRules.BalanceIn(1200m, 1200m, 1500m).ShouldBe(0m);
    }
}
