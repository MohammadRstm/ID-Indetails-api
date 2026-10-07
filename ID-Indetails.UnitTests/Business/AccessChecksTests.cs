using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class AccessChecksTests
{
    private static UserAccess With(params string[] permissions)
        => new(7, RoleCodes.Admin, DomainCodes.Design, permissions, false, [DomainCodes.Design]);

    [Fact]
    public void A_service_lets_through_whoever_holds_one_of_the_permissions_and_refuses_the_rest()
    {
        var actor = With(PermissionCodes.FinancialView);
        actor.Require(PermissionCodes.FinancialView).ShouldBeSameAs(actor);
        actor.Require(PermissionCodes.ExpenseManage, PermissionCodes.FinancialView).ShouldBeSameAs(actor);
        Should.Throw<ForbiddenException>(() => actor.Require(PermissionCodes.CompanyView)).Code.ShouldBe(ErrorCodes.Forbidden);
        Should.Throw<ForbiddenException>(() => With().Require(PermissionCodes.SalaryView));
    }
}
