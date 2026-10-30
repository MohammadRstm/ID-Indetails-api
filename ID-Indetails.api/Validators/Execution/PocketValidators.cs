using FluentValidation;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Execution;

public sealed class PocketContributionRequestValidator : AbstractValidator<PocketContributionRequest>
{
    public PocketContributionRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).WithMessage("Choose a site.");
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
        RuleFor(x => x.Note).ClientText("The note", PocketRules.MaxNoteLength);
    }
}

public sealed class CreatePocketRequestRequestValidator : AbstractValidator<CreatePocketRequestRequest>
{
    public CreatePocketRequestRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).WithMessage("Choose a site.");
        RuleFor(x => x.Note).ClientText("The note", PocketRules.MaxNoteLength);
    }
}

public sealed class ApprovePocketRequestRequestValidator : AbstractValidator<ApprovePocketRequestRequest>
{
    public ApprovePocketRequestRequestValidator()
    {
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
        RuleFor(x => x.Note).ClientText("The note", PocketRules.MaxNoteLength);
    }
}

public sealed class DenyPocketRequestRequestValidator : AbstractValidator<DenyPocketRequestRequest>
{
    public DenyPocketRequestRequestValidator()
    {
        RuleFor(x => x.Note).ClientText("The note", PocketRules.MaxNoteLength);
    }
}

public sealed class PocketEntryListQueryValidator : AbstractValidator<PocketEntryListQuery>
{
    public PocketEntryListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The range must end on or after its start.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}
