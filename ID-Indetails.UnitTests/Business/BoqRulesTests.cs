using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class BoqRulesTests
{
    private static BoqRules.Line L(decimal quantity, decimal waste, decimal rate) => new(quantity, waste, rate);

    private static BoqRules.Caller Caller(long id = 1, bool draft = false, bool money = false, bool pricing = false) => new(id, draft, money, pricing);

    [Fact]
    public void Numbers_are_the_prefix_the_year_and_the_sequence()
    {
        BoqRules.Number("BQ", 2026, 7).ShouldBe("BQ-2026-007");
        BoqRules.Number("VO", 2027, 1234).ShouldBe("VO-2027-1234");
        BoqRules.PrefixOf(BoqKind.Client).ShouldBe("BQ");
        BoqRules.PrefixOf(BoqKind.Vo).ShouldBe("VO");
        BoqRules.PrefixOf(BoqKind.Internal).ShouldBeNull();
    }

    [Fact]
    public void A_title_defaults_by_kind_and_a_copy_is_cut_to_fit()
    {
        BoqRules.DefaultTitle(BoqKind.Client).ShouldBe("Client BOQ");
        BoqRules.DefaultTitle(BoqKind.Internal).ShouldBe("Internal draft");
        BoqRules.DefaultTitle(BoqKind.Vo).ShouldBe("Variation order");

        BoqRules.CopyTitle("Villa").ShouldBe("Villa (copy)");
        var long120 = new string('x', BoqRules.MaxTitleLength);
        var copy = BoqRules.CopyTitle(long120);
        copy.Length.ShouldBe(BoqRules.MaxTitleLength);
        copy.ShouldEndWith(" (copy)");
    }

    [Theory]
    [InlineData(10, 0.05, 10.5)]
    [InlineData(2.5, 0.1, 2.75)]
    [InlineData(7, 0, 7)]
    [InlineData(0.125, 0.01, 0.13)]
    [InlineData(3, 0.3333, 4.00)]
    [InlineData(0, 0.5, 0)]
    public void The_final_quantity_adds_the_waste_and_rounds_to_two_decimals(double quantity, double waste, double expected)
        => BoqRules.FinalQuantity((decimal)quantity, (decimal)waste).ShouldBe((decimal)expected);

    [Fact]
    public void A_line_total_is_the_final_quantity_times_the_rate_to_the_cent()
    {
        // 10 + 5% = 10.50 × 12.345 = 129.6225
        BoqRules.LineTotal(10, 0.05m, 12.345m).ShouldBe(129.62m);
        // the rounded final quantity is what is priced: 3 + 33.33% = 3.9999 → 4.00, so 40.00 and not 39.999
        BoqRules.LineTotal(3, 0.3333m, 10m).ShouldBe(40m);
        // halves go away from zero
        BoqRules.LineTotal(1, 0, 0.125m).ShouldBe(0.13m);
        BoqRules.LineTotal(1, 0, -0.125m).ShouldBe(-0.13m);
    }

    [Fact]
    public void A_negative_rate_lowers_the_total()
    {
        BoqRules.LineTotal(4, 0, -250m).ShouldBe(-1000m);
        BoqRules.TotalsOf([L(10, 0, 100m), L(2, 0, -150m)], BoqAdjustmentType.None, null).Total.ShouldBe(700m);
    }

    [Fact]
    public void Without_an_adjustment_the_total_is_the_subtotal()
    {
        var totals = BoqRules.TotalsOf([L(10, 0.05m, 12.345m), L(1, 0, 100m)], BoqAdjustmentType.None, null);

        totals.Subtotal.ShouldBe(229.62m);
        totals.Adjustment.ShouldBe(0m);
        totals.Total.ShouldBe(229.62m);
        BoqRules.TotalsOf([], BoqAdjustmentType.None, null).Total.ShouldBe(0m);
    }

    [Theory]
    [InlineData(-0.05, -50, 950)]
    [InlineData(0.10, 100, 1100)]
    [InlineData(0, 0, 1000)]
    [InlineData(-0.99, -990, 10)]
    public void A_percent_adjustment_is_a_fraction_of_the_subtotal(double fraction, double adjustment, double total)
    {
        var totals = BoqRules.TotalsOf([L(10, 0, 100m)], BoqAdjustmentType.Percent, (decimal)fraction);

        totals.Subtotal.ShouldBe(1000m);
        totals.Adjustment.ShouldBe((decimal)adjustment);
        totals.Total.ShouldBe((decimal)total);
    }

    [Fact]
    public void A_percent_adjustment_is_rounded_to_the_cent()
    {
        // 333.33 × 0.1234 = 41.1328...
        var totals = BoqRules.TotalsOf([L(1, 0, 333.33m)], BoqAdjustmentType.Percent, 0.1234m);

        totals.Adjustment.ShouldBe(41.13m);
        totals.Total.ShouldBe(374.46m);
    }

    [Theory]
    [InlineData(900, -100)]
    [InlineData(1000, 0)]
    [InlineData(1250.5, 250.5)]
    [InlineData(0, -1000)]
    public void A_total_adjustment_sets_the_new_total(double value, double adjustment)
    {
        var totals = BoqRules.TotalsOf([L(10, 0, 100m)], BoqAdjustmentType.Total, (decimal)value);

        totals.Adjustment.ShouldBe((decimal)adjustment);
        totals.Total.ShouldBe((decimal)value);
    }

    [Fact]
    public void A_variation_order_can_adjust_a_negative_subtotal()
    {
        var lines = new[] { L(1, 0, -2000m) };

        BoqRules.TotalsOf(lines, BoqAdjustmentType.Percent, -0.1m).Total.ShouldBe(-1800m);
        BoqRules.TotalsOf(lines, BoqAdjustmentType.Total, -1500m).Total.ShouldBe(-1500m);
    }

    [Fact]
    public void The_work_price_is_the_agreed_client_boq_and_the_agreed_vos()
    {
        BoqRules.WorkPrice(0, []).ShouldBe(0m);
        BoqRules.WorkPrice(10_000m, []).ShouldBe(10_000m);
        BoqRules.WorkPrice(10_000m, [1500m, -500m]).ShouldBe(11_000m);
    }

    [Fact]
    public void Supervision_and_tva_come_from_the_total_with_the_projects_rates()
    {
        var price = ExecutionPricing.PriceOf(10_000m, 0.10m, 0.11m);

        price.Supervision.ShouldBe(1000m);
        price.Net.ShouldBe(11_000m);
        // TVA is on the work, never on supervision
        price.Tva.ShouldBe(1100m);
        price.Gross.ShouldBe(12_100m);

        var negative = ExecutionPricing.PriceOf(-500m, 0.10m, 0.11m);
        (negative.Supervision, negative.Tva, negative.Gross).ShouldBe((-50m, -55m, -605m));
        ExecutionPricing.PriceOf(0, 0.10m, 0.11m).Gross.ShouldBe(0m);
    }

    [Fact]
    public void Only_an_execution_client_project_has_boqs()
    {
        BoqRules.CheckSite(DomainCodes.Execution, BillingType.Client).ShouldBeNull();
        BoqRules.CheckSite(DomainCodes.Design, BillingType.Client)!.Code.ShouldBe(ErrorCodes.BoqExecutionOnly);
        BoqRules.CheckSite(DomainCodes.Execution, BillingType.Internal)!.Code.ShouldBe(ErrorCodes.BoqExecutionOnly);
    }

    [Fact]
    public void A_variation_order_needs_the_client_boq_agreed_first()
    {
        BoqRules.CheckCanCreate(BoqKind.Vo, clientAgreed: false)!.Code.ShouldBe(ErrorCodes.BoqNotAgreedYet);
        BoqRules.CheckCanCreate(BoqKind.Vo, clientAgreed: true).ShouldBeNull();
        BoqRules.CheckCanCreate(BoqKind.Client, clientAgreed: false).ShouldBeNull();
        BoqRules.CheckCanCreate(BoqKind.Internal, clientAgreed: false).ShouldBeNull();
    }

    [Fact]
    public void An_agreed_boq_is_locked()
    {
        BoqRules.CheckEditable(BoqStatus.Draft).ShouldBeNull();
        BoqRules.CheckEditable(BoqStatus.Agreed)!.Code.ShouldBe(ErrorCodes.BoqLocked);
    }

    [Fact]
    public void Only_a_variation_order_has_a_negative_rate_a_reason_or_a_negative_total()
    {
        var negative = new[] { L(1, 0, -100m) };
        var plain = new[] { L(1, 0, 100m) };

        BoqRules.CheckContent(BoqKind.Vo, "Smaller kitchen", BoqAdjustmentType.None, null, negative).ShouldBeNull();
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.None, null, negative)!.Code.ShouldBe(ErrorCodes.BoqNegativeRate);
        BoqRules.CheckContent(BoqKind.Internal, null, BoqAdjustmentType.None, null, negative)!.Code.ShouldBe(ErrorCodes.BoqNegativeRate);

        var noReason = BoqRules.CheckContent(BoqKind.Vo, "  ", BoqAdjustmentType.None, null, plain)!;
        (noReason.Code, noReason.Field).ShouldBe((ErrorCodes.ValidationFailed, "reason"));
        BoqRules.CheckContent(BoqKind.Client, "Why", BoqAdjustmentType.None, null, plain)!.Field.ShouldBe("reason");

        BoqRules.CheckContent(BoqKind.Vo, "Less", BoqAdjustmentType.Total, -300m, plain).ShouldBeNull();
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.Total, -300m, plain)!.Field.ShouldBe("adjustmentValue");
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.Total, 0m, plain).ShouldBeNull();
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.Percent, -0.5m, plain).ShouldBeNull();
    }

    [Fact]
    public void A_boq_has_a_limit_of_lines_and_totals_that_fit_what_is_stored()
    {
        var many = Enumerable.Repeat(L(1, 0, 1m), BoqRules.MaxLines + 1).ToList();
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.None, null, many)!.Field.ShouldBe("lines");
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.None, null, many.Take(BoqRules.MaxLines).ToList()).ShouldBeNull();

        var huge = new[] { L(999_999_999m, 0, 999_999_999m) };
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.None, null, huge)!.Field.ShouldBe("lines");
        // a percent adjustment can push a total over the limit as well
        var nearly = new[] { L(1, 0, 600_000_000_000m) };
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.None, null, nearly).ShouldBeNull();
        BoqRules.CheckContent(BoqKind.Client, null, BoqAdjustmentType.Percent, 1m, nearly)!.Field.ShouldBe("lines");
    }

    [Fact]
    public void An_internal_draft_or_an_empty_boq_is_never_sent()
    {
        BoqRules.CheckSendable(BoqKind.Client, 3).ShouldBeNull();
        BoqRules.CheckSendable(BoqKind.Vo, 1).ShouldBeNull();
        BoqRules.CheckSendable(BoqKind.Internal, 3)!.Code.ShouldBe(ErrorCodes.BoqInternal);
        BoqRules.CheckSendable(BoqKind.Client, 0)!.Code.ShouldBe(ErrorCodes.BoqEmpty);
        BoqRules.CheckClientFacing(BoqKind.Internal)!.Code.ShouldBe(ErrorCodes.BoqInternal);
        BoqRules.CheckClientFacing(BoqKind.Vo).ShouldBeNull();
    }

    [Fact]
    public void A_client_boq_is_agreed_once_and_a_vo_after_it()
    {
        BoqRules.CheckAgreeable(BoqKind.Client, BoqStatus.Draft, 2, clientAgreed: false).ShouldBeNull();
        BoqRules.CheckAgreeable(BoqKind.Client, BoqStatus.Draft, 2, clientAgreed: true)!.Code.ShouldBe(ErrorCodes.BoqAlreadyAgreed);
        BoqRules.CheckAgreeable(BoqKind.Vo, BoqStatus.Draft, 1, clientAgreed: true).ShouldBeNull();
        BoqRules.CheckAgreeable(BoqKind.Vo, BoqStatus.Draft, 1, clientAgreed: false)!.Code.ShouldBe(ErrorCodes.BoqNotAgreedYet);
        BoqRules.CheckAgreeable(BoqKind.Internal, BoqStatus.Draft, 1, clientAgreed: true)!.Code.ShouldBe(ErrorCodes.BoqInternal);
        BoqRules.CheckAgreeable(BoqKind.Client, BoqStatus.Agreed, 1, clientAgreed: false)!.Code.ShouldBe(ErrorCodes.BoqLocked);
        BoqRules.CheckAgreeable(BoqKind.Client, BoqStatus.Draft, 0, clientAgreed: false)!.Code.ShouldBe(ErrorCodes.BoqEmpty);
    }

    [Fact]
    public void An_agreement_is_withdrawn_unless_variation_orders_stand_on_it()
    {
        BoqRules.CheckWithdrawable(BoqKind.Client, BoqStatus.Agreed, agreedVos: 0).ShouldBeNull();
        BoqRules.CheckWithdrawable(BoqKind.Client, BoqStatus.Agreed, agreedVos: 2)!.Code.ShouldBe(ErrorCodes.BoqHasAgreedVos);
        // a VO is withdrawn whatever else is agreed
        BoqRules.CheckWithdrawable(BoqKind.Vo, BoqStatus.Agreed, agreedVos: 2).ShouldBeNull();
        BoqRules.CheckWithdrawable(BoqKind.Vo, BoqStatus.Draft, agreedVos: 0)!.Code.ShouldBe(ErrorCodes.BoqNotAgreed);
        BoqRules.CheckWithdrawable(BoqKind.Internal, BoqStatus.Draft, agreedVos: 0)!.Code.ShouldBe(ErrorCodes.BoqInternal);
    }

    [Fact]
    public void Variation_orders_can_not_take_the_work_price_below_nothing()
    {
        BoqRules.CheckWorkPrice(0).ShouldBeNull();
        BoqRules.CheckWorkPrice(12_000m).ShouldBeNull();
        BoqRules.CheckWorkPrice(-0.01m)!.Code.ShouldBe(ErrorCodes.BoqPriceBelowZero);
    }

    [Fact]
    public void An_author_reads_and_writes_their_own_internal_draft_and_pricing_managers_every_one()
    {
        const long author = 5;
        var drafter = Caller(id: author, draft: true);
        var otherDrafter = Caller(id: 6, draft: true);
        var pricing = Caller(id: 7, pricing: true);
        var viewer = Caller(id: 8, money: true);
        var lostPermission = Caller(id: author);

        BoqRules.CanRead(BoqKind.Internal, author, drafter).ShouldBeTrue();
        BoqRules.CanWrite(BoqKind.Internal, author, drafter).ShouldBeTrue();
        BoqRules.CanRead(BoqKind.Internal, author, otherDrafter).ShouldBeFalse();
        BoqRules.CanWrite(BoqKind.Internal, author, otherDrafter).ShouldBeFalse();
        BoqRules.CanRead(BoqKind.Internal, author, pricing).ShouldBeTrue();
        BoqRules.CanWrite(BoqKind.Internal, author, pricing).ShouldBeTrue();
        BoqRules.CanRead(BoqKind.Internal, author, viewer).ShouldBeFalse();
        // an author without the permission any more reads nothing of it
        BoqRules.CanRead(BoqKind.Internal, author, lostPermission).ShouldBeFalse();
    }

    [Fact]
    public void Client_boqs_and_vos_are_read_with_money_or_pricing_and_written_with_pricing_alone()
    {
        foreach (var kind in new[] { BoqKind.Client, BoqKind.Vo })
        {
            BoqRules.CanRead(kind, 1, Caller(money: true)).ShouldBeTrue();
            BoqRules.CanRead(kind, 1, Caller(pricing: true)).ShouldBeTrue();
            BoqRules.CanRead(kind, 1, Caller(draft: true)).ShouldBeFalse();
            BoqRules.CanWrite(kind, 1, Caller(money: true)).ShouldBeFalse();
            BoqRules.CanWrite(kind, 1, Caller(draft: true, money: true)).ShouldBeFalse();
            BoqRules.CanWrite(kind, 1, Caller(pricing: true)).ShouldBeTrue();
            BoqRules.CanCreate(kind, Caller(draft: true, money: true)).ShouldBeFalse();
            BoqRules.CanCreate(kind, Caller(pricing: true)).ShouldBeTrue();
        }

        BoqRules.CanCreate(BoqKind.Internal, Caller(draft: true)).ShouldBeTrue();
        BoqRules.CanCreate(BoqKind.Internal, Caller(pricing: true)).ShouldBeFalse();
    }

    [Fact]
    public void A_copy_is_the_latest_save_only_within_the_millisecond_the_time_is_stored_to()
    {
        var saved = new DateTime(2026, 10, 29, 12, 0, 0, 123, DateTimeKind.Utc);

        BoqRules.SameSave(saved, saved).ShouldBeTrue();
        // the database keeps milliseconds: the time a save answered with may be a little finer, either way
        BoqRules.SameSave(saved, saved.AddTicks(4_567)).ShouldBeTrue();
        BoqRules.SameSave(saved, saved.AddTicks(-4_567)).ShouldBeTrue();
        BoqRules.SameSave(saved, saved.AddMilliseconds(1)).ShouldBeFalse();
        BoqRules.SameSave(saved, saved.AddSeconds(-30)).ShouldBeFalse();
    }
}
