using FluentValidation;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Billing;

public sealed class PaymentListQueryValidator : AbstractValidator<PaymentListQuery>
{
    public PaymentListQueryValidator()
    {
        RuleFor(x => x.Domain).ValidDomain().When(x => x.Domain is not null);
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false)
            .Must((x, amount) => MoneyRules.FitsInUsd(amount, x.FxRate)).WithMessage("At this exchange rate, the amount in US dollars is too large.");
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.FxRate).Rate();
        RuleFor(x => x.Method).IsInEnum().WithMessage("Method must be bank, cheque, cash or other.");
        RuleFor(x => x.Reference).ClientText("Reference", 100);
        RuleFor(x => x.Note).ClientText("Note", 500);
        RuleFor(x => x.InvoiceId).GreaterThan(0).When(x => x.InvoiceId is not null).WithMessage("Invoice is invalid.");
    }
}

public sealed class ApplyPaymentRequestValidator : AbstractValidator<ApplyPaymentRequest>
{
    public ApplyPaymentRequestValidator()
    {
        RuleFor(x => x.InvoiceId).GreaterThan(0).WithMessage("Invoice is required.");
    }
}
