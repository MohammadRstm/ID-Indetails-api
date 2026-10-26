using FluentValidation;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Execution;

public sealed class SiteSummaryQueryValidator : AbstractValidator<SiteSummaryQuery>
{
    public SiteSummaryQueryValidator()
    {
        RuleFor(x => x.Ids)
            .Must(ids => ids is { Length: >= 1 and <= EmployeeListQueryValidator.MaxPageSize })
            .WithMessage($"Ask for 1 to {EmployeeListQueryValidator.MaxPageSize} sites at a time.");
    }
}
