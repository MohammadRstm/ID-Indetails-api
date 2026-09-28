using FluentValidation;
using ID_Indetails.api.DTO.Request.Activity;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Activity;

public sealed class ActivityListQueryValidator : AbstractValidator<ActivityListQuery>
{
    public ActivityListQueryValidator()
    {
        RuleFor(x => x.Group).Must(g => g is null || (Enum.IsDefined(g.Value) && g != ActivityGroup.Other))
            .WithMessage("Group must be work, money, billing, projects, people or company.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or more.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}
