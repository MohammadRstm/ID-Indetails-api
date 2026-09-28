using ID_Indetails.api.DTO.Request.Permissions;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Permissions;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class PermissionValidatorTests
{
    private static readonly UpdateUserPermissionsRequestValidator Validator = new();

    private static PermissionChangeRequest Change(string code, PermissionChangeAction action = PermissionChangeAction.Grant, string? reason = null)
        => new() { Code = code, Action = action, Reason = reason };

    [Fact]
    public void A_list_of_distinct_changes_is_valid()
        => Validator.Validate(new UpdateUserPermissionsRequest
        {
            Changes = [Change("invoice:void"), Change("client:view", PermissionChangeAction.Reset, "Back to normal")],
        }).IsValid.ShouldBeTrue();

    [Fact]
    public void At_least_one_change_is_required()
        => Validator.Validate(new UpdateUserPermissionsRequest()).IsValid.ShouldBeFalse();

    [Fact]
    public void The_same_permission_cannot_appear_twice()
        => Validator.Validate(new UpdateUserPermissionsRequest
        {
            Changes = [Change("invoice:void"), Change("invoice:void", PermissionChangeAction.Revoke)],
        }).IsValid.ShouldBeFalse();

    [Fact]
    public void Codes_actions_and_reasons_are_checked()
    {
        Validator.Validate(new UpdateUserPermissionsRequest { Changes = [Change("")] }).IsValid.ShouldBeFalse();
        Validator.Validate(new UpdateUserPermissionsRequest { Changes = [Change("x:y", (PermissionChangeAction)42)] })
            .IsValid.ShouldBeFalse();
        Validator.Validate(new UpdateUserPermissionsRequest { Changes = [Change("x:y", reason: new string('a', 501))] })
            .IsValid.ShouldBeFalse();
    }
}
