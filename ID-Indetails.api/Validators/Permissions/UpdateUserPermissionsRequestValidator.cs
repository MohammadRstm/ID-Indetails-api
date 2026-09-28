using FluentValidation;
using ID_Indetails.api.DTO.Request.Permissions;

namespace ID_Indetails.api.Validators.Permissions;

/// <summary>
/// Shape of the request only. Whether the codes exist, and who may change whose permissions,
/// is decided in the service (<c>Business/Auth/PermissionChangePolicy</c>).
/// </summary>
public sealed class UpdateUserPermissionsRequestValidator : AbstractValidator<UpdateUserPermissionsRequest>
{
    public const int MaxChanges = 100;

    public UpdateUserPermissionsRequestValidator()
    {
        RuleFor(x => x.Changes)
            .NotEmpty().WithMessage("List at least one change.")
            .Must(c => c.Count <= MaxChanges).WithMessage($"At most {MaxChanges} changes at once.")
            .Must(c => c.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() == c.Count)
            .WithMessage("Each permission may appear only once.");

        RuleForEach(x => x.Changes).ChildRules(change =>
        {
            change.RuleFor(c => c.Code)
                .NotEmpty().WithMessage("Permission code is required.")
                .MaximumLength(64).WithMessage("Permission code must be at most 64 characters.");
            change.RuleFor(c => c.Action).IsInEnum().WithMessage("Action must be grant, revoke or reset.");
            change.RuleFor(c => c.Reason).MaximumLength(500).WithMessage("Reason must be at most 500 characters.");
        });
    }
}
