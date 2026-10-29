using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Enums;
using Shouldly;
using static ID_Indetails.api.Business.Execution.AccountFiguresRules;

namespace ID_Indetails.UnitTests.Business;

public class AccountFiguresRulesTests
{
    private const long Concrete = 1;
    private const long Finishing = 2;
    private const long Permits = 3;

    private static ProjectFinance.Costs Costs(decimal siteExpenses = 0, decimal ground = 0, decimal staff = 0, decimal other = 0)
        => new(staff, other, siteExpenses, ground, []);

    private static PaymentRow Paid(decimal amount, long? label = null) => new(label, amount);

    private static ExpenseRow Spent(
        decimal? usd, long? label = null, SiteExpenseCategory category = SiteExpenseCategory.Materials, string? payee = null)
        => new(label, category, payee, usd);

    private static Inputs Account(
        ProjectFinance.Costs? costs = null,
        IReadOnlyList<PaymentRow>? payments = null,
        IReadOnlyList<ExpenseRow>? expenses = null,
        decimal clientTotal = 0,
        decimal supervisionDue = 0,
        decimal supervisionTaken = 0,
        decimal toPocket = 0,
        decimal fromPocket = 0,
        IReadOnlyList<long>? labels = null)
        => new(clientTotal, supervisionDue, costs ?? Costs(), supervisionTaken, toPocket, fromPocket,
            labels ?? [Concrete, Finishing, Permits], payments ?? [], expenses ?? []);

    [Fact]
    public void Cash_in_account_is_received_less_cost_supervision_taken_and_the_pocket_moves()
    {
        var figures = Build(Account(
            costs: Costs(siteExpenses: 120_000, ground: 30_000, staff: 5_000, other: 1_000),
            payments: [Paid(200_000), Paid(100_000, Concrete)],
            supervisionTaken: 20_000,
            toPocket: 10_000,
            fromPocket: 4_000));

        figures.Received.ShouldBe(300_000);
        // 300,000 − 156,000 − 20,000 − 10,000 + 4,000
        figures.CashInAccount.ShouldBe(118_000);
    }

    [Fact]
    public void Cash_in_account_can_be_below_zero()
    {
        Build(Account(costs: Costs(siteExpenses: 500), payments: [Paid(100)], supervisionTaken: 50)).CashInAccount.ShouldBe(-450);
    }

    [Fact]
    public void The_label_rows_add_up_to_cash_in_account()
    {
        var figures = Build(Account(
            costs: Costs(siteExpenses: 70_000, ground: 12_500.50m, staff: 800, other: 99.99m),
            payments: [Paid(100_000, Concrete), Paid(40_000, Finishing), Paid(25_000.25m, Concrete), Paid(60_000)],
            expenses:
            [
                Spent(30_000, Concrete), Spent(5_000.40m, Concrete), Spent(12_000, Finishing), Spent(8_000, Permits),
                // unlabelled and unconverted site expenses are part of the cost the project reports (or left out of it), not of a label
                Spent(14_999.60m), Spent(null, Finishing),
            ],
            supervisionTaken: 15_000,
            toPocket: 6_000,
            fromPocket: 1_500));

        figures.ByLabel.Sum(r => r.Rest).ShouldBe(figures.CashInAccount);
        figures.ByLabel.Sum(r => r.Received).ShouldBe(figures.Received);
        // every cost of the project is in exactly one row
        figures.ByLabel.Sum(r => r.Spent).ShouldBe(83_400.49m);
        figures.ByLabel.Sum(r => r.Other).ShouldBe(15_000 + 6_000 - 1_500);
        figures.ByLabel.Select(r => r.Spent).ShouldBe([35_000.40m, 12_000m, 8_000m, 28_400.09m]);
    }

    [Fact]
    public void Each_label_has_what_it_received_and_spent_and_the_unlabelled_row_the_rest()
    {
        var figures = Build(Account(
            costs: Costs(siteExpenses: 50_000, ground: 10_000, staff: 2_000, other: 500),
            payments: [Paid(100_000, Concrete), Paid(25_000, Concrete), Paid(40_000, Finishing), Paid(60_000)],
            expenses: [Spent(30_000, Concrete), Spent(12_000, Finishing), Spent(8_000)],
            supervisionTaken: 20_000,
            toPocket: 5_000,
            fromPocket: 1_000));

        figures.ByLabel.ShouldBe(
        [
            new LabelRow(Concrete, 125_000, 30_000, 0),
            new LabelRow(Finishing, 40_000, 12_000, 0),
            // received with no label; the 8,000 unlabelled expense, ground labour, staff and other are the rest of the 62,500 cost
            new LabelRow(null, 60_000, 20_500, 24_000),
        ]);
        figures.ByLabel.Select(r => r.Rest).ShouldBe([95_000, 28_000, 15_500]);
        figures.CashInAccount.ShouldBe(138_500);
    }

    [Fact]
    public void The_unlabelled_row_carries_labour_supervision_and_the_pocket_when_nothing_is_labelled()
    {
        var figures = Build(Account(
            costs: Costs(siteExpenses: 1_000, ground: 4_000, staff: 300),
            payments: [Paid(10_000)],
            expenses: [Spent(1_000)],
            supervisionTaken: 2_000,
            toPocket: 700,
            fromPocket: 200));

        var only = figures.ByLabel.ShouldHaveSingleItem();
        only.LabelId.ShouldBeNull();
        only.Received.ShouldBe(10_000);
        only.Spent.ShouldBe(5_300);
        only.Other.ShouldBe(2_500);
        only.Rest.ShouldBe(2_200);
        only.Rest.ShouldBe(figures.CashInAccount);
    }

    [Fact]
    public void Labelled_rows_carry_no_other_amounts()
    {
        var figures = Build(Account(
            costs: Costs(siteExpenses: 100),
            payments: [Paid(500, Permits)],
            expenses: [Spent(100, Permits)],
            supervisionTaken: 50,
            toPocket: 10));

        figures.ByLabel[0].Other.ShouldBe(0);
        figures.ByLabel[1].Other.ShouldBe(60);
    }

    [Fact]
    public void A_site_with_nothing_recorded_has_only_the_unlabelled_row_at_zero()
    {
        var figures = Build(Account());

        figures.ByLabel.ShouldBe([new LabelRow(null, 0, 0, 0)]);
        figures.CashInAccount.ShouldBe(0);
        figures.ByCategory.ShouldBeEmpty();
        figures.ByPayee.ShouldBeEmpty();
    }

    [Fact]
    public void Label_rows_follow_the_labels_order_not_their_ids_then_the_unlabelled_row_last()
    {
        var figures = Build(Account(
            payments: [Paid(1, Permits), Paid(1, Concrete), Paid(1)],
            expenses: [Spent(2, Finishing)],
            costs: Costs(siteExpenses: 2),
            labels: [Permits, Finishing, Concrete]));

        figures.ByLabel.Select(r => r.LabelId).ShouldBe([Permits, Finishing, Concrete, null]);
    }

    [Fact]
    public void A_label_whose_only_expense_has_no_exchange_rate_still_has_its_row_with_nothing_spent()
    {
        var figures = Build(Account(costs: Costs(), expenses: [Spent(null, Concrete)]));

        figures.ByLabel.ShouldBe([new LabelRow(Concrete, 0, 0, 0), new LabelRow(null, 0, 0, 0)]);
        figures.ByCategory.ShouldBeEmpty();
        figures.ByPayee.ShouldBeEmpty();
    }

    [Fact]
    public void Labels_with_nothing_filed_under_them_have_no_row()
    {
        var figures = Build(Account(payments: [Paid(10, Finishing)], labels: [Concrete, Finishing, Permits]));

        figures.ByLabel.Select(r => r.LabelId).ShouldBe([Finishing, null]);
    }

    [Fact]
    public void A_label_missing_from_the_order_is_still_counted_after_the_ordered_ones()
    {
        var figures = Build(Account(payments: [Paid(5, 99), Paid(5, Concrete)], labels: [Concrete]));

        figures.ByLabel.Select(r => r.LabelId).ShouldBe([Concrete, 99, null]);
        figures.ByLabel.Sum(r => r.Rest).ShouldBe(figures.CashInAccount);
    }

    [Fact]
    public void Supervision_left_is_what_is_due_less_what_was_taken_and_goes_negative_when_over()
    {
        Build(Account(supervisionDue: 63_011.70m, supervisionTaken: 20_000)).SupervisionLeft.ShouldBe(43_011.70m);
        Build(Account(supervisionDue: 80_000, supervisionTaken: 80_000)).SupervisionLeft.ShouldBe(0);
        Build(Account(supervisionDue: 80_000, supervisionTaken: 90_000)).SupervisionLeft.ShouldBe(-10_000);
        Build(Account(supervisionDue: 0, supervisionTaken: 500)).SupervisionLeft.ShouldBe(-500);
    }

    [Fact]
    public void Taking_more_supervision_than_is_due_still_comes_off_the_account()
    {
        var figures = Build(Account(payments: [Paid(100_000)], supervisionDue: 8_000, supervisionTaken: 9_000));

        figures.CashInAccount.ShouldBe(91_000);
        figures.ByLabel.Single().Other.ShouldBe(9_000);
    }

    [Fact]
    public void Rest_to_pay_is_the_client_total_less_received_and_goes_negative_when_they_paid_ahead()
    {
        // 787,646.20 of work at 8% supervision and 11% TVA, as in the Mila 2 check figures with TVA
        Build(Account(clientTotal: 937_298.98m, payments: [Paid(810_000)])).RestToPay.ShouldBe(127_298.98m);
        Build(Account(clientTotal: 1_000, payments: [Paid(1_000)])).RestToPay.ShouldBe(0);
        Build(Account(clientTotal: 1_000, payments: [Paid(700, Concrete), Paid(500)])).RestToPay.ShouldBe(-200);
        Build(Account(clientTotal: 1_000)).RestToPay.ShouldBe(1_000);
    }

    [Fact]
    public void Categories_are_largest_first_and_leave_out_expenses_with_no_rate()
    {
        var figures = Build(Account(expenses:
        [
            Spent(300, category: SiteExpenseCategory.Fees),
            Spent(900, category: SiteExpenseCategory.Materials),
            Spent(250, category: SiteExpenseCategory.Fees),
            Spent(550, category: SiteExpenseCategory.Contractor),
            Spent(null, category: SiteExpenseCategory.Equipment),
        ]));

        figures.ByCategory.ShouldBe(
        [
            new CategoryRow(SiteExpenseCategory.Materials, 900),
            new CategoryRow(SiteExpenseCategory.Contractor, 550),
            new CategoryRow(SiteExpenseCategory.Fees, 550),
        ],
        customMessage: "equal totals keep the categories' own order (contractor before fees), so the order never flickers");
    }

    [Fact]
    public void Equal_categories_follow_the_categories_own_order()
    {
        var figures = Build(Account(expenses:
        [
            Spent(100, category: SiteExpenseCategory.Other),
            Spent(100, category: SiteExpenseCategory.Contractor),
            Spent(100, category: SiteExpenseCategory.Materials),
        ]));

        figures.ByCategory.Select(c => c.Category).ShouldBe(
            [SiteExpenseCategory.Materials, SiteExpenseCategory.Contractor, SiteExpenseCategory.Other]);
    }

    [Fact]
    public void Payees_are_grouped_by_their_text_shown_as_last_typed_and_largest_first()
    {
        // newest recorded first, as the repository gives them
        var figures = Build(Account(expenses:
        [
            Spent(400, payee: "  Al-Rashid Steel "),
            Spent(1_000, payee: "Beirut Cranes"),
            Spent(250, payee: "al-rashid  steel"),
            Spent(60, payee: "   "),
            Spent(40, payee: null),
            Spent(null, payee: "Unpriced Ltd"),
        ]));

        figures.ByPayee.ShouldBe(
        [
            new PayeeRow("Beirut Cranes", 1_000),
            new PayeeRow("Al-Rashid Steel", 650),
            new PayeeRow(null, 100),
        ]);
    }

    [Fact]
    public void Payees_with_equal_totals_follow_their_names_and_no_payee_comes_last()
    {
        var figures = Build(Account(expenses:
        [
            Spent(100, payee: null),
            Spent(100, payee: "Zahle Hardware"),
            Spent(100, payee: "abc Tools"),
        ]));

        figures.ByPayee.Select(p => p.Payee).ShouldBe(["abc Tools", "Zahle Hardware", null]);
    }

    [Fact]
    public void Categories_and_payees_each_add_up_to_the_site_expenses_that_have_a_rate()
    {
        var figures = Build(Account(expenses:
        [
            Spent(120.25m, Concrete, SiteExpenseCategory.Materials, "A"),
            Spent(79.75m, null, SiteExpenseCategory.Fees, "B"),
            Spent(300, Finishing, SiteExpenseCategory.Contractor, null),
            Spent(null, Finishing, SiteExpenseCategory.Contractor, "C"),
        ]));

        figures.ByCategory.Sum(c => c.Spent).ShouldBe(500);
        figures.ByPayee.Sum(p => p.Spent).ShouldBe(500);
    }
}
