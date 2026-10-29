using FluentValidation;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Execution;

public sealed class TakeSupervisionRequestValidator : AbstractValidator<TakeSupervisionRequest>
{
    public TakeSupervisionRequestValidator()
    {
        RuleFor(x => x.AmountUsd).Amount("Amount", allowZero: false)
            .Must(v => v <= SupervisionRules.MaxAmount).WithMessage("Amount is too large.");
        RuleFor(x => x.TakenOn).ThisCentury("The date");
        RuleFor(x => x.Note).ClientText("The note", SupervisionRules.MaxNoteLength);
    }
}
