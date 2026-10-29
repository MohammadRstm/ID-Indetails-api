using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectRulesTests
{
    private static ProjectRules.Setup Design(BillingType billing, long? client, decimal? revenue = null, decimal? supervision = null)
        => new(DomainCodes.Design, billing, client, revenue, supervision);

    private static ProjectRules.Setup Execution(BillingType billing, long? client, decimal? revenue = null, decimal? supervision = 0.1m)
        => new(DomainCodes.Execution, billing, client, revenue, supervision);

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
    public void Only_execution_projects_have_supervision()
    {
        ProjectRules.CheckSetup(Design(BillingType.Client, 1, supervision: 0.1m))!.Field.ShouldBe("supervisionRate");
        ProjectRules.CheckSetup(Execution(BillingType.Client, 1)).ShouldBeNull();
        // a site may be created without its pricing, for someone with pricing to set later
        ProjectRules.CheckSetup(Execution(BillingType.Client, 1, supervision: null)).ShouldBeNull();
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
    public void Only_client_work_has_a_payment_to_change()
    {
        ProjectRules.CheckHasPayment(BillingType.Client).ShouldBeNull();
        ProjectRules.CheckHasPayment(BillingType.Internal)!.Field.ShouldBe("revenue");
    }

    [Fact]
    public void A_payment_change_must_change_an_amount()
    {
        ProjectRules.CheckPaymentChanges(true, "revenue").ShouldBeNull();
        var denial = ProjectRules.CheckPaymentChanges(false, "projectFee")!;
        denial.Field.ShouldBe("projectFee");
        denial.Code.ShouldBe(ErrorCodes.ValidationFailed);
    }
}
