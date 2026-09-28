using FluentValidation;
using ID_Indetails.api.DTO.Request.Payroll;

namespace ID_Indetails.api.Validators.Payroll;

public sealed class ProjectPayQueryValidator : AbstractValidator<ProjectPayQuery>
{
    public ProjectPayQueryValidator()
    {
        RuleFor(x => x.Unit).IsInEnum().WithMessage("Unit must be week, month or quarter.");
        RuleFor(x => x.ProjectId).GreaterThan(0).When(x => x.ProjectId is not null).WithMessage("Project is invalid.");
    }
}
