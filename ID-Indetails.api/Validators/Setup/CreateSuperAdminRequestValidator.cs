using FluentValidation;
using ID_Indetails.api.DTO.Request.Setup;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Setup;

public sealed class CreateSuperAdminRequestValidator : AbstractValidator<CreateSuperAdminRequest>
{
    public CreateSuperAdminRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");
        RuleFor(x => x.Position).MaximumLength(120).WithMessage("Position must be at most 120 characters.");
    }
}
