using FluentValidation;
using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Requirements;

public sealed class SaveRequirementRequestValidator : AbstractValidator<SaveRequirementRequest>
{
    public const int MaxAssignees = 50;

    public SaveRequirementRequestValidator()
    {
        RuleFor(x => x.Text).ClientText("Text", 500, required: true);
        RuleFor(x => x.Deadline).ThisCentury("The deadline");
        RuleFor(x => x.AssigneeIds)
            .NotNull().WithMessage("Assignees must be a list.")
            .Must(ids => ids is null || ids.Count <= MaxAssignees).WithMessage($"At most {MaxAssignees} assignees.")
            .Must(ids => ids is null || ids.All(id => id > 0)).WithMessage("Assignee is invalid.");
    }
}
