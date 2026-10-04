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
        RuleFor(x => x.Category)
            .Must(c => c is null || ExpenseRules.Categories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", ExpenseRules.Categories)}.");
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}
