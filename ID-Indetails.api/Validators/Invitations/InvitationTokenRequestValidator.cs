using FluentValidation;
using ID_Indetails.api.DTO.Request.Invitations;

namespace ID_Indetails.api.Validators.Invitations;

public sealed class InvitationTokenRequestValidator : AbstractValidator<InvitationTokenRequest>
{
    public InvitationTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("The invitation token is missing.")
            .MaximumLength(200).WithMessage("The invitation token is invalid.");
    }
}
