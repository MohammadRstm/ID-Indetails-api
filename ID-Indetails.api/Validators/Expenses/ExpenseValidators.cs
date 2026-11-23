using FluentValidation;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Expenses;

public sealed class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.Label).ClientText("Label", 200, required: true);
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.SpentOn).ThisCentury("The date");
        // one of Design's expense categories, checked when the expense is saved
        RuleFor(x => x.Category).MaximumLength(40).WithMessage("Category is up to 40 characters.");
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}
