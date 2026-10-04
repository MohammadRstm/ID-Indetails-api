using FluentValidation;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Auth;

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).LookupEmail();
    }
}
