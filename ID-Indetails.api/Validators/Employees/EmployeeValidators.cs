using FluentValidation;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Employees;

/// <summary>
/// Shape of the people-management requests only. Who may change whom is decided by
/// <c>Business/Employees/EmployeeRules</c> in the service.
/// </summary>
public sealed class EmployeeListQueryValidator : AbstractValidator<EmployeeListQuery>
{
    public const int MaxPageSize = 100;

    public EmployeeListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId is not null).WithMessage("Team is invalid.");
        RuleFor(x => x.Role).ValidRole().When(x => x.Role is not null);
        RuleFor(x => x.Domain).ValidDomain().When(x => x.Domain is not null);
        RuleFor(x => x.Status).IsInEnum().WithMessage("Status must be invited, active or deactivated.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize).WithMessage($"Page size must be between 1 and {MaxPageSize}.");
    }
}

public sealed class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(100).WithMessage("First name must be at most 100 characters.");
        RuleFor(x => x.LastName).MaximumLength(100).WithMessage("Last name must be at most 100 characters.");
        RuleFor(x => x.Phone).Phone();
        RuleFor(x => x.Position).MaximumLength(120).WithMessage("Position must be at most 120 characters.");
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId is not null).WithMessage("Team is invalid.");
    }
}

public sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        RuleFor(x => x.Role).ValidRole();
        RuleFor(x => x.DomainCode).ValidDomain().When(x => x.DomainCode is not null);
        RuleFor(x => x.Position).MaximumLength(120).WithMessage("Position must be at most 120 characters.");
    }
}

public sealed class MoveEmployeeRequestValidator : AbstractValidator<MoveEmployeeRequest>
{
    public MoveEmployeeRequestValidator()
    {
        RuleFor(x => x.DomainCode).ValidDomain();
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId is not null).WithMessage("Team is invalid.");
    }
}

public sealed class ChangePayRequestValidator : AbstractValidator<ChangePayRequest>
{
    public ChangePayRequestValidator()
    {
        RuleFor(x => x.Basis).IsInEnum().WithMessage("Pay basis must be hourly or monthly.");
        RuleFor(x => x.Amount).Amount("Pay amount", allowZero: false);
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.Note).MaximumLength(255).WithMessage("Note must be at most 255 characters.");
    }
}

public sealed class ChangeEmailRequestValidator : AbstractValidator<ChangeEmailRequest>
{
    public ChangeEmailRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
    }
}

public sealed class ChangeMyEmailRequestValidator : AbstractValidator<ChangeMyEmailRequest>
{
    public ChangeMyEmailRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.")
            .MaximumLength(ValidationRules.PasswordMaxLength).WithMessage("Current password is too long.");
    }
}

public sealed class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");
        RuleFor(x => x.Phone).Phone();
    }
}

internal static class EmployeeRuleExtensions
{
    public static IRuleBuilderOptions<T, string?> ValidRole<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(r => r is not null && RoleCodes.All.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", RoleCodes.All)}.");

    public static IRuleBuilderOptions<T, string?> ValidDomain<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(d => d is not null && DomainCodes.All.Contains(d))
            .WithMessage($"Domain must be one of: {string.Join(", ", DomainCodes.All)}.");

    public static IRuleBuilderOptions<T, string?> Phone<T>(this IRuleBuilder<T, string?> rule)
        => rule.MaximumLength(32).WithMessage("Phone must be at most 32 characters.");
}
