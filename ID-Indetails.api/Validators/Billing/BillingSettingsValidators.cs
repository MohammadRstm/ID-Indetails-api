using FluentValidation;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Validators.Clients;

namespace ID_Indetails.api.Validators.Billing;

public sealed class SaveBillingSettingsRequestValidator : AbstractValidator<SaveBillingSettingsRequest>
{
    public SaveBillingSettingsRequestValidator()
    {
        RuleFor(x => x.InvoicePrefix)
            .Matches("^[A-Z][A-Z0-9]{0,7}$")
            .WithMessage("Invoice prefix must be 1 to 8 capital letters or digits, starting with a letter.");
        RuleFor(x => x.DefaultTermsDays).InclusiveBetween(0, 365).WithMessage("Payment terms must be between 0 and 365 days.");
        RuleFor(x => x.BankAccount).ClientText("Bank account", 60);
        RuleFor(x => x.BankName).ClientText("Bank name", 120);
        RuleFor(x => x.Iban).ClientText("IBAN", 34);
        RuleFor(x => x.Swift)
            .Matches("^[A-Za-z0-9]{8}([A-Za-z0-9]{3})?$").WithMessage("SWIFT code must be 8 or 11 letters or digits.");
        RuleFor(x => x.TermsText).ClientText("Terms", 2000);
        RuleFor(x => x.Footer).ClientText("Footer", 500);
    }
}

public sealed class ExchangeRateListQueryValidator : AbstractValidator<ExchangeRateListQuery>
{
    public ExchangeRateListQueryValidator()
    {
        RuleFor(x => x.Currency).Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter code.");
    }
}
