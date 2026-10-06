using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectRulesTests
{
    private static ProjectRules.Setup Design(BillingType billing, long? client, decimal? revenue = null, decimal? fee = null, decimal? markup = null)
        => new(DomainCodes.Design, billing, client, revenue, fee, markup);

    private static ProjectRules.Setup Execution(BillingType billing, long? client, decimal? revenue = null, decimal? fee = 1000, decimal? markup = 0.1m)
        => new(DomainCodes.Execution, billing, client, revenue, fee, markup);

    [Fact]
    public void Design_client_work_needs_a_client_and_internal_work_has_none()
    {
        ProjectRules.CheckSetup(Design(BillingType.Client, 1, 5000)).ShouldBeNull();
        ProjectRules.CheckSetup(Design(BillingType.Internal, null)).ShouldBeNull();
        ProjectRules.CheckSetup(Design(BillingType.Client, null))!.Field.ShouldBe("clientId");
        ProjectRules.CheckSetup(Design(BillingType.Internal, 1))!.Field.ShouldBe("clientId");
        ProjectRules.CheckSetup(Design(BillingType.Internal, null, 10))!.Field.ShouldBe("revenue");
    }

    [Fact]
    public void Only_execution_projects_have_a_fee_and_markup()
    {
        ProjectRules.CheckSetup(Design(BillingType.Client, 1, fee: 100))!.Field.ShouldBe("projectFee");
        ProjectRules.CheckSetup(Execution(BillingType.Client, 1)).ShouldBeNull();
        ProjectRules.CheckSetup(Execution(BillingType.Client, 1, fee: null))!.Field.ShouldBe("projectFee");
        ProjectRules.CheckSetup(Execution(BillingType.Client, 1, markup: null))!.Field.ShouldBe("projectFee");
    }

    [Fact]
    public void Execution_is_always_client_work_with_a_calculated_revenue()
    {
        ProjectRules.CheckSetup(Execution(BillingType.Internal, null))!.Field.ShouldBe("billingType");
        ProjectRules.CheckSetup(Execution(BillingType.Client, 1, revenue: 500))!.Field.ShouldBe("revenue");
        ProjectRules.CheckSetup(Execution(BillingType.Client, null))!.Field.ShouldBe("clientId");
    }

    [Fact]
    public void The_owner_is_an_active_admin_of_the_domain_or_a_super_admin()
    {
        ProjectRules.CheckOwner(new(RoleCodes.Admin, DomainCodes.Design, UserStatus.Active), DomainCodes.Design).ShouldBeNull();
        ProjectRules.CheckOwner(new(RoleCodes.Admin, DomainCodes.Design, UserStatus.Invited), DomainCodes.Design).ShouldBeNull();
        ProjectRules.CheckOwner(new(RoleCodes.Employee, DomainCodes.Design, UserStatus.Active), DomainCodes.Design)!
            .Code.ShouldBe(ErrorCodes.OwnerNotAllowed);
        ProjectRules.CheckOwner(new(RoleCodes.SuperAdmin, null, UserStatus.Active), DomainCodes.Execution).ShouldBeNull();
        ProjectRules.CheckOwner(new(RoleCodes.Admin, DomainCodes.Execution, UserStatus.Active), DomainCodes.Design)!
            .Code.ShouldBe(ErrorCodes.OwnerNotAllowed);
        ProjectRules.CheckOwner(new(RoleCodes.Employee, DomainCodes.Design, UserStatus.Deactivated), DomainCodes.Design)!
            .Code.ShouldBe(ErrorCodes.OwnerNotAllowed);
    }

    [Fact]
    public void Locked_money_and_billed_clients_cannot_change()
    {
        ProjectRules.CheckMoneyEditable(isLocked: false).ShouldBeNull();
        ProjectRules.CheckMoneyEditable(isLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectLocked);
        ProjectRules.CheckClientChange(1, 1, hasBillingRecords: true).ShouldBeNull();
        ProjectRules.CheckClientChange(1, 2, hasBillingRecords: false).ShouldBeNull();
        ProjectRules.CheckClientChange(1, null, hasBillingRecords: true)!.Code.ShouldBe(ErrorCodes.ProjectHasBilling);
        ProjectRules.CheckClient(isArchived: true)!.Code.ShouldBe(ErrorCodes.ClientIsArchived);
    }

    [Fact]
    public void Material_client_price_is_the_manual_price_or_cost_plus_markup()
    {
        ExecutionPricing.ClientPrice(new(200m, null), 0.10m).ShouldBe(220m);
        ExecutionPricing.ClientPrice(new(33.33m, null), 0.15m).ShouldBe(38.33m);
        ExecutionPricing.ClientPrice(new(30m, 50m), 0.10m).ShouldBe(50m);
    }

    [Fact]
    public void Agreed_amount_is_material_charges_plus_the_fee()
    {
        ExecutionPricing.AgreedAmount([new(200m, null), new(30m, 50m)], 0.10m, 1000m).ShouldBe(1270m);
        ExecutionPricing.AgreedAmount([], 0.10m, 1000m).ShouldBe(1000m);
    }
}
