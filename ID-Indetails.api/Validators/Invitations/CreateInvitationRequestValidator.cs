using FluentValidation;
using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Invitations;

/// <summary>
/// Shape of the request only. Who may invite whom is decided by
/// <c>Business/Auth/InvitationPolicy</c> in the service.
/// </summary>
public sealed class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();

        RuleFor(x => x.Role)
            .Must(r => RoleCodes.All.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", RoleCodes.All)}.");

        RuleFor(x => x.DomainCode)
            .Must(d => d is null || DomainCodes.All.Contains(d))
            .WithMessage($"Domain must be one of: {string.Join(", ", DomainCodes.All)}.");

        RuleFor(x => x.DomainCode)
            .Null().When(x => x.Role == RoleCodes.SuperAdmin)
            .WithMessage("Super admins do not belong to a domain.");

        RuleFor(x => x.TeamId)
            .GreaterThan(0).When(x => x.TeamId is not null)
            .WithMessage("Team is invalid.");

        RuleFor(x => x.Position)
            .MaximumLength(120).WithMessage("Position must be at most 120 characters.");

        RuleFor(x => x.Pay)
            .NotNull().When(x => RoleCodes.All.Contains(x.Role) && PayRules.HasFixedSalary(x.Role))
            .WithMessage("Enter their monthly salary: admins and super admins are paid a fixed salary.");

        RuleFor(x => x.Pay!).ChildRules(pay =>
        {
            pay.RuleFor(p => p.Basis).IsInEnum().WithMessage("Pay basis must be hourly or monthly.");
            pay.RuleFor(p => p.Amount).Amount("Pay amount", allowZero: false);
            pay.RuleFor(p => p.OvertimeRate).OvertimeRate(p => p.Basis, p => p.Amount);
            pay.RuleFor(p => p.Currency).Currency();
        }).When(x => x.Pay is not null);
    }
}
