using FluentValidation;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;
using ID_Indetails.api.Validators.Labour;

namespace ID_Indetails.api.Validators.Projects;

public sealed class ProjectListQueryValidator : AbstractValidator<ProjectListQuery>
{
    public ProjectListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
        RuleFor(x => x.Status).IsInEnum().WithMessage("Status must be active, hold, completed or archived.");
        RuleFor(x => x.BillingType).IsInEnum().WithMessage("Billing type must be client or internal.");
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId is not null).WithMessage("Client is invalid.");
        RuleFor(x => x.Domain).ValidBusiness().When(x => x.Domain is not null);
        RuleFor(x => x.OwnerId).GreaterThan(0).When(x => x.OwnerId is not null).WithMessage("Owner is invalid.");
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100).When(x => x.Year is not null).WithMessage("Year must be between 2000 and 2100.");
        RuleFor(x => x.Quarter).InclusiveBetween(1, 4).When(x => x.Quarter is not null).WithMessage("Quarter must be between 1 and 4.");
        RuleFor(x => x.Year).NotNull().When(x => x.Quarter is not null).WithMessage("A quarter needs a year.");
        RuleFor(x => x.Sort).IsInEnum().WithMessage("Sort must be start, activity, hours, progress, name or client.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Title).ClientText("Title", 200, required: true);
        RuleFor(x => x.Description).ClientText("Description", 2000);
        RuleFor(x => x.Location).ClientText("Location", 200);
        RuleFor(x => x.Icon).ClientText("Icon", 40);
        // the client looks the icon up by name: only names, nothing a lookup could turn into something else
        RuleFor(x => x.Icon).Matches("^[A-Za-z0-9_-]+$").WithMessage("Icon must be one of the app's icon names.");
        RuleFor(x => x.Tint).ProjectTint();
        RuleFor(x => x.DomainCode).ValidBusiness().When(x => x.DomainCode is not null);
        RuleFor(x => x.BillingType).IsInEnum().WithMessage("Billing type must be client or internal.");
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId is not null).WithMessage("Client is invalid.");
        RuleFor(x => x.OwnerId).GreaterThan(0).When(x => x.OwnerId is not null).WithMessage("Owner is invalid.");
        RuleFor(x => x.Revenue).Amount("Revenue");
        RuleFor(x => x.ProjectFee).WorkPriceNotTyped();
        RuleFor(x => x.SupervisionRate).SupervisionRate();
        RuleFor(x => x.TvaRate).TvaRate();
        RuleFor(x => x.LabourDefaults!).SetValidator(new SetLabourDefaultsRequestValidator()).When(x => x.LabourDefaults is not null);
        RuleForEach(x => x.DepartmentIds).GreaterThan(0).WithMessage("A department is invalid.");
    }
}

public sealed class SetProjectDepartmentsRequestValidator : AbstractValidator<SetProjectDepartmentsRequest>
{
    public SetProjectDepartmentsRequestValidator()
    {
        RuleFor(x => x.DepartmentIds).NotNull().Must(ids => ids.Count > 0).WithMessage("Choose the departments working on it.");
        RuleForEach(x => x.DepartmentIds).GreaterThan(0).WithMessage("A department is invalid.");
    }
}

public sealed class CreateDesignVoRequestValidator : AbstractValidator<CreateDesignVoRequest>
{
    public CreateDesignVoRequestValidator()
    {
        RuleFor(x => (string?)x.Title).ClientText("Title", DesignVoRules.MaxTitleLength, required: true).OverridePropertyName("title");
        RuleFor(x => x.Split).IsInEnum().WithMessage("Split by the shares or to chosen departments.");
        RuleFor(x => x.Lines).NotNull().Must(l => l.Count is > 0 and <= DesignVoRules.MaxLines)
            .WithMessage($"Add between 1 and {DesignVoRules.MaxLines} lines.");
        RuleForEach(x => x.Lines).Must(l => l.Description is { } d && d.Trim().Length <= DesignVoRules.MaxDescriptionLength)
            .WithMessage($"A line's description is at most {DesignVoRules.MaxDescriptionLength} characters.");
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).Must(a => a.DepartmentId > 0).WithMessage("A department is invalid.");
    }
}

public sealed class SetDepartmentSharesRequestValidator : AbstractValidator<SetDepartmentSharesRequest>
{
    public SetDepartmentSharesRequestValidator()
    {
        RuleFor(x => x.Shares).NotNull().Must(s => s.Count > 0).WithMessage("Give each department its share.");
        RuleForEach(x => x.Shares).Must(s => s.DepartmentId > 0).WithMessage("A department is invalid.");
        RuleFor(x => x.Reason).ClientText("Reason", ProjectDepartmentRules.MaxReasonLength);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Title).ClientText("Title", 200, required: true);
        RuleFor(x => x.Description).ClientText("Description", 2000);
        RuleFor(x => x.Location).ClientText("Location", 200);
        RuleFor(x => x.Icon).ClientText("Icon", 40);
        // the client looks the icon up by name: only names, nothing a lookup could turn into something else
        RuleFor(x => x.Icon).Matches("^[A-Za-z0-9_-]+$").WithMessage("Icon must be one of the app's icon names.");
        RuleFor(x => x.Tint).ProjectTint();
        RuleFor(x => x.OwnerId).GreaterThan(0).WithMessage("Owner is required.");
    }
}

public sealed class UpdateProjectBillingRequestValidator : AbstractValidator<UpdateProjectBillingRequest>
{
    public UpdateProjectBillingRequestValidator()
    {
        RuleFor(x => x.BillingType).IsInEnum().WithMessage("Billing type must be client or internal.");
        RuleFor(x => x.ClientId).GreaterThan(0).When(x => x.ClientId is not null).WithMessage("Client is invalid.");
    }
}

public sealed class UpdateProjectPaymentRequestValidator : AbstractValidator<UpdateProjectPaymentRequest>
{
    public UpdateProjectPaymentRequestValidator()
    {
        RuleFor(x => x.Revenue).Amount("Revenue");
        RuleFor(x => x.TvaRate).TvaRate();
        RuleFor(x => x.Description).ClientText("The description", ProjectRules.MaxPaymentDescriptionLength, required: true);
    }
}

public sealed class UpdateProjectPricingRequestValidator : AbstractValidator<UpdateProjectPricingRequest>
{
    public UpdateProjectPricingRequestValidator()
    {
        RuleFor(x => x.ProjectFee).WorkPriceNotTyped();
        RuleFor(x => x.SupervisionRate).SupervisionRate();
        RuleFor(x => x.TvaRate).TvaRate();
        RuleFor(x => x.Description).ClientText("The description", ProjectRules.MaxPaymentDescriptionLength, required: true);
    }
}

internal static class ProjectRuleExtensions
{
    public static IRuleBuilderOptions<T, decimal?> TvaRate<T>(this IRuleBuilder<T, decimal?> rule)
        => rule.InclusiveBetween(0m, Tva.MaxRate).WithMessage("TVA must be between 0% and 100%.");

    public static IRuleBuilderOptions<T, decimal?> SupervisionRate<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .InclusiveBetween(0m, 1m).WithMessage("Supervision must be between 0% and 100%.")
            .Must(v => v is null || decimal.Round(v.Value, 4) == v).WithMessage("Supervision can have at most 4 decimals.");

    public static IRuleBuilderOptions<T, decimal?> WorkPriceNotTyped<T>(this IRuleBuilder<T, decimal?> rule)
        => rule.Null().WithMessage("The work price comes from the agreed BOQs: it can't be typed in.");

    public static IRuleBuilderOptions<T, string?> ProjectTint<T>(this IRuleBuilder<T, string?> rule)
        => rule.Matches("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")
            .WithMessage("Tint must be a colour like #1F6FEB.");
}

public sealed class ProjectReportQueryValidator : AbstractValidator<ProjectReportQuery>
{
    public ProjectReportQueryValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Kind must be internal or client.");
        RuleForEach(x => x.Sections).IsInEnum().WithMessage("Unknown report section.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The range must end on or after its start.");
    }
}

public sealed class SetProjectBudgetRequestValidator : AbstractValidator<SetProjectBudgetRequest>
{
    public SetProjectBudgetRequestValidator()
    {
        RuleFor(x => x.Reason).ClientText("The reason", BudgetRules.MaxReasonLength, required: true);
        RuleFor(x => x.EffectiveFrom).ThisCentury("The start");
        RuleFor(x => x.Lines)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Budget at least one line.")
            .Must(l => l.Count >= 1).WithMessage("Budget at least one line.")
            .Must(l => l.Count <= BudgetRules.MaxLines).WithMessage($"A budget has at most {BudgetRules.MaxLines} lines.")
            .Must(l => l.Where(x => x is not null).GroupBy(x => (x.TeamId, x.Category)).All(g => g.Count() == 1))
            .WithMessage("Each department or category is budgeted once.");
        RuleForEach(x => x.Lines)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A line is missing.")
            .ChildRules(line =>
            {
                line.RuleFor(l => l.TeamId)
                    .Must((l, teamId) => (teamId is null) != (l.Category is null))
                    .WithMessage("A line is a department or a category, not both.");
                line.RuleFor(l => l.TeamId).GreaterThan(0).When(l => l.TeamId is not null).WithMessage("The department is invalid.");
                line.RuleFor(l => l.Category)
                    .Must(c => c is null || BudgetRules.AllCategories.Contains(c))
                    .WithMessage($"The category must be one of {string.Join(", ", BudgetRules.AllCategories)}.");
                line.RuleFor(l => l.Amount)
                    .Amount("The amount")
                    .Must(a => a <= BudgetRules.MaxAmount).WithMessage("The amount can be at most 100,000,000 USD.");
            });
    }
}
