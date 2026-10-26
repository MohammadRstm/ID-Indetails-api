using FluentValidation;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Execution;

public sealed class BoxTransferRequestValidator : AbstractValidator<BoxTransferRequest>
{
    public BoxTransferRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).WithMessage("Choose a project.");
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
        RuleFor(x => x.Note).ClientText("The note", BoxRules.MaxNoteLength);
    }
}

public sealed class BoxDecisionRequestValidator : AbstractValidator<BoxDecisionRequest>
{
    public BoxDecisionRequestValidator()
    {
        RuleFor(x => x.Decision).Must(d => d is BoxChoice.Remove or BoxChoice.Keep)
            .WithMessage("Remove the project's money from the box, or keep it.");
    }
}

public sealed class BoxEntryListQueryValidator : AbstractValidator<BoxEntryListQuery>
{
    public BoxEntryListQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}
