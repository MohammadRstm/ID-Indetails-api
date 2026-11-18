using FluentValidation;
using ID_Indetails.api.DTO.Request.DevSignIn;

namespace ID_Indetails.api.Validators.DevSignIn;

public sealed class DevSignInRequestValidator : AbstractValidator<DevSignInRequest>
{
    public DevSignInRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("Pick an account.");
    }
}

public sealed class DevPortalSignInRequestValidator : AbstractValidator<DevPortalSignInRequest>
{
    public DevPortalSignInRequestValidator()
    {
        RuleFor(x => x.AccountId).GreaterThan(0).WithMessage("Pick an account.");
    }
}
