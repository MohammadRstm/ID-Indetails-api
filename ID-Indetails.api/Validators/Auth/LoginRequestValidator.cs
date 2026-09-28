using FluentValidation;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();

        // No strength rules on login: existing passwords are only checked against their hash.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(ValidationRules.PasswordMaxLength).WithMessage("Password is too long.");
    }
}
