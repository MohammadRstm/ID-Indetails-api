using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Payroll;

public sealed class SalaryListQueryValidator : AbstractValidator<SalaryListQuery>
{
    public SalaryListQueryValidator()
    {
        RuleFor(x => x.Month).Month().When(x => x.Month is not null);
        RuleFor(x => x.Domain)
            .Must(d => d is null || DomainCodes.All.Contains(d))
            .WithMessage($"Domain must be one of: {string.Join(", ", DomainCodes.All)}.");
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or more.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class MyHoursQueryValidator : AbstractValidator<MyHoursQuery>
{
    /// <summary>A thousand pages is 12,000 months; beyond it the month arithmetic would overflow.</summary>
    public const int MaxPage = 1000;

    public MyHoursQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage).WithMessage($"Page must be between 1 and {MaxPage}.");
    }
}

public sealed class SalaryMonthQueryValidator : AbstractValidator<SalaryMonthQuery>
{
    public SalaryMonthQueryValidator()
    {
        RuleFor(x => x.Month).Month().When(x => x.Month is not null);
    }
}

public sealed class AddHourExcuseRequestValidator : AbstractValidator<AddHourExcuseRequest>
{
    private const int MaxMinutes = 31 * 24 * 60;

    public AddHourExcuseRequestValidator()
    {
        RuleFor(x => x.Month).Month();
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Kind must be minutes, days or whole_month.");

        RuleFor(x => x.Minutes)
            .NotNull().WithMessage("Say how many minutes to excuse.")
            .InclusiveBetween(1, MaxMinutes).WithMessage("Minutes must be between 1 and a month.")
            .When(x => x.Kind == ExcuseKind.Minutes);
        RuleFor(x => x.Minutes).Null().When(x => x.Kind != ExcuseKind.Minutes).WithMessage("Minutes are only for a minutes excuse.");

        RuleFor(x => x.Days)
            .NotEmpty().WithMessage("Choose at least one day.")
            .Must(d => d.Count <= 31).WithMessage("At most 31 days.")
            .When(x => x.Kind == ExcuseKind.Days);
        RuleFor(x => x.Days).Empty().When(x => x.Kind != ExcuseKind.Days).WithMessage("Days are only for a days excuse.");

        RuleFor(x => x.Note).MaximumLength(255).WithMessage("Note must be at most 255 characters.");
    }
}

internal static class PayrollRuleExtensions
{
    public static IRuleBuilderOptions<T, string?> Month<T>(this IRuleBuilder<T, string?> rule)
        => rule.Matches("^[0-9]{4}-(0[1-9]|1[0-2])$").WithMessage("Month must look like 2026-06.");
}
