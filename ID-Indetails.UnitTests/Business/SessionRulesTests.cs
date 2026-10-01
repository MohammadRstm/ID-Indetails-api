using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Target = ID_Indetails.api.Business.Employees.EmployeeRules.Target;

namespace ID_Indetails.UnitTests.Business;

public class SessionRulesTests
{
    private static readonly DateTime Ended = new(2026, 10, 1, 9, 30, 15, 400, DateTimeKind.Utc);

    private static readonly UserAccess SuperAdmin = new(
        1, RoleCodes.SuperAdmin, null, [PermissionCodes.EmployeeManage, PermissionCodes.EmployeeInviteSuperAdmin], false, DomainCodes.All);

    private static readonly UserAccess DesignAdmin = new(2, RoleCodes.Admin, DomainCodes.Design, [PermissionCodes.EmployeeManage], false, [DomainCodes.Design]);

    private static Target Employee(UserStatus status = UserStatus.Active) => new(10, RoleCodes.Employee, DomainCodes.Design, status);

    [Fact]
    public void Tokens_work_as_usual_for_someone_whose_sessions_were_never_ended()
        => SessionRules.Accepts(Ended, sessionsRevokedAt: null, sessionsValidFrom: null).ShouldBeTrue();

    [Fact]
    public void While_sign_in_is_suspended_no_token_works_however_new()
        => SessionRules.Accepts(Ended.AddHours(1), sessionsRevokedAt: Ended, sessionsValidFrom: Ended).ShouldBeFalse();

    [Fact]
    public void After_a_restore_tokens_from_before_the_end_stay_refused_and_new_ones_work()
    {
        SessionRules.Accepts(Ended.AddMinutes(-30), sessionsRevokedAt: null, sessionsValidFrom: Ended).ShouldBeFalse();
        // iat is whole seconds, so the very second of the end can't tell before from after: it is refused
        SessionRules.Accepts(new DateTime(2026, 10, 1, 9, 30, 15, DateTimeKind.Utc), null, Ended).ShouldBeFalse();
        SessionRules.Accepts(new DateTime(2026, 10, 1, 9, 30, 16, DateTimeKind.Utc), null, Ended).ShouldBeTrue();
    }

    [Fact]
    public void Only_a_super_admin_ends_or_restores_sessions_and_never_their_own()
    {
        EmployeeRules.CheckEndSessions(DesignAdmin, Employee(), alreadyEnded: false).ShouldNotBeNull();
        EmployeeRules.CheckRestoreSignIn(DesignAdmin, Employee(), ended: true).ShouldNotBeNull();
        EmployeeRules.CheckEndSessions(SuperAdmin, new Target(1, RoleCodes.SuperAdmin, null, UserStatus.Active), alreadyEnded: false)
            .ShouldNotBeNull().Code.ShouldBe(ErrorCodes.EmployeeChangeNotAllowed);
        EmployeeRules.CheckEndSessions(SuperAdmin, Employee(), alreadyEnded: false).ShouldBeNull();
        EmployeeRules.CheckRestoreSignIn(SuperAdmin, Employee(), ended: true).ShouldBeNull();
    }

    [Fact]
    public void Sessions_end_only_on_a_working_account_and_only_once()
    {
        EmployeeRules.CheckEndSessions(SuperAdmin, Employee(UserStatus.Invited), alreadyEnded: false).ShouldNotBeNull();
        EmployeeRules.CheckEndSessions(SuperAdmin, Employee(UserStatus.Deactivated), alreadyEnded: false).ShouldNotBeNull();
        EmployeeRules.CheckEndSessions(SuperAdmin, Employee(), alreadyEnded: true).ShouldNotBeNull().Code.ShouldBe(ErrorCodes.SessionsAlreadyEnded);
        EmployeeRules.CheckRestoreSignIn(SuperAdmin, Employee(), ended: false).ShouldNotBeNull().Code.ShouldBe(ErrorCodes.SessionsNotEnded);
    }
}
