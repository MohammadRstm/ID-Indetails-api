using FluentValidation;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Auth;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("The reset token is missing.")
            .MaximumLength(200).WithMessage("The reset token is invalid.");

        RuleFor(x => x.Password).NewPassword();
    }
}
