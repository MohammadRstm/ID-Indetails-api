using FluentValidation;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Portal;

public sealed class InvitePortalClientRequestValidator : AbstractValidator<InvitePortalClientRequest>
{
    public InvitePortalClientRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
    }
}

public sealed class PortalInvitationTokenRequestValidator : AbstractValidator<PortalInvitationTokenRequest>
{
    public PortalInvitationTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("The invitation token is missing.")
            .MaximumLength(200).WithMessage("The invitation token is invalid.");
    }
}

public sealed class AcceptPortalInvitationRequestValidator : AbstractValidator<AcceptPortalInvitationRequest>
{
    public AcceptPortalInvitationRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("The invitation token is missing.")
            .MaximumLength(200).WithMessage("The invitation token is invalid.");

        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");
        RuleFor(x => x.Password).NewPassword();
    }
}
