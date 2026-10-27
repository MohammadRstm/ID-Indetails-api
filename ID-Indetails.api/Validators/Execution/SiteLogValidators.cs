using FluentValidation;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Execution;

public sealed class SaveSiteLogRequestValidator : AbstractValidator<SaveSiteLogRequest>
{
    public SaveSiteLogRequestValidator()
    {
        RuleFor(x => x.Body).ClientText("The log", SiteTeamRules.MaxLogLength, required: true);
    }
}

public sealed class SiteLogListQueryValidator : AbstractValidator<SiteLogListQuery>
{
    public SiteLogListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The range must end on or after its start.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class UpdateProjectStaffRequestValidator : AbstractValidator<UpdateProjectStaffRequest>
{
    public UpdateProjectStaffRequestValidator()
    {
        RuleFor(x => x.StaffIds).NotNull().Must(ids => ids.Count is >= 1 and <= 50)
            .WithMessage("Choose between 1 and 50 staff members to oversee the site.");
    }
}
