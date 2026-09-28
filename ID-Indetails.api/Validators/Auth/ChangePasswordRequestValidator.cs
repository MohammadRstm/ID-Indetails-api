using FluentValidation;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Auth;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.")
            .MaximumLength(ValidationRules.PasswordMaxLength).WithMessage("Current password is too long.");

        RuleFor(x => x.NewPassword)
            .NewPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}
