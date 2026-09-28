using FluentValidation;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Invitations;

public sealed class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("The invitation token is missing.")
            .MaximumLength(200).WithMessage("The invitation token is invalid.");

        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");

        RuleFor(x => x.Phone)
            .MaximumLength(32).WithMessage("Phone must be at most 32 characters.")
            .Matches(@"^\+?[0-9 ()\-]{6,32}$").When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Enter a valid phone number.");

        RuleFor(x => x.Password).NewPassword();
    }
}
