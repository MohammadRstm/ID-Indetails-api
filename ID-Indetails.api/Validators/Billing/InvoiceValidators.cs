using FluentValidation;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Billing;

public sealed class InvoiceListQueryValidator : AbstractValidator<InvoiceListQuery>
{
    public InvoiceListQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithMessage("Status must be draft, sent, partial, paid or void.");
        RuleFor(x => x.Domain).ValidDomain().When(x => x.Domain is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or more.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class SaveInvoiceRequestValidator : AbstractValidator<SaveInvoiceRequest>
{
    public const int MaxLines = 100;

    public SaveInvoiceRequestValidator()
    {
        RuleFor(x => x.Lines)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Add at least one line.")
            .Must(l => l.Count is >= 1 and <= MaxLines).WithMessage($"An invoice has 1 to {MaxLines} lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).ClientText("Description", 500, required: true);
            line.RuleFor(l => l.Amount).Amount("Amount", allowZero: false);
        });
        RuleFor(x => x.TermsDays).InclusiveBetween(0, 365).When(x => x.TermsDays is not null)
            .WithMessage("Payment terms must be between 0 and 365 days.");
        RuleFor(x => x.Notes).ClientText("Notes", 2000);
        RuleFor(x => x.BillToName).ClientText("Bill-to name", 200);
        RuleFor(x => x.BillToContact).ClientText("Bill-to contact", 255);
    }
}

public sealed class RecordDeliveryRequestValidator : AbstractValidator<RecordDeliveryRequest>
{
    public RecordDeliveryRequestValidator()
    {
        RuleFor(x => x.Channel).IsInEnum().WithMessage("Channel must be email, whatsapp or download.");
        RuleFor(x => x.Recipient).ClientText("Recipient", 255);
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}

public sealed class EmailDocumentRequestValidator : AbstractValidator<EmailDocumentRequest>
{
    public EmailDocumentRequestValidator()
    {
        RuleFor(x => x.To).ValidEmail();
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}

public sealed class VoidRequestValidator : AbstractValidator<VoidRequest>
{
    public VoidRequestValidator()
    {
        RuleFor(x => x.Reason).ClientText("Reason", 500, required: true);
    }
}
