using FluentValidation;
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
        RuleFor(x => x.ProjectFee).Amount("Project fee");
        RuleFor(x => x.MaterialMarkup).MaterialMarkup();
        RuleFor(x => x.InitialPayment!).ChildRules(payment =>
        {
            payment.RuleFor(p => p.Amount).Amount("Initial payment", allowZero: false)
                .Must((p, amount) => MoneyRules.FitsInUsd(amount, p.FxRate)).WithMessage("At this exchange rate, the initial payment in US dollars is too large.");
            payment.RuleFor(p => p.Currency).Currency();
            payment.RuleFor(p => p.FxRate).Rate();
            payment.RuleFor(p => p.Method).IsInEnum().WithMessage("Method must be bank, cheque, cash or other.");
            payment.RuleFor(p => p.Reference).ClientText("Reference", 100);
            payment.RuleFor(p => p.Note).ClientText("Note", 500);
        }).When(x => x.InitialPayment is not null);
        RuleFor(x => x.LabourDefaults!).SetValidator(new SetLabourDefaultsRequestValidator()).When(x => x.LabourDefaults is not null);
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
        RuleFor(x => x.Revenue).Amount("Revenue");
    }
}

public sealed class UpdateProjectPricingRequestValidator : AbstractValidator<UpdateProjectPricingRequest>
{
    public UpdateProjectPricingRequestValidator()
    {
        RuleFor(x => x.ProjectFee).Amount("Project fee");
        RuleFor(x => (decimal?)x.MaterialMarkup).MaterialMarkup().OverridePropertyName("MaterialMarkup");
    }
}

internal static class ProjectRuleExtensions
{
    public static IRuleBuilderOptions<T, string?> ProjectTint<T>(this IRuleBuilder<T, string?> rule)
        => rule.Matches("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")
            .WithMessage("Tint must be a colour like #1F6FEB.");

    public static IRuleBuilderOptions<T, decimal?> MaterialMarkup<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(v => v is null || v is >= 0 and <= 5).WithMessage("Material markup must be between 0 and 5 (0% to 500%).")
            .Must(v => v is null || decimal.Round(v.Value, 4) == v).WithMessage("Material markup can have at most 4 decimals.");
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
