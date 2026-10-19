using FluentValidation;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Clients;

public sealed class ClientListQueryValidator : AbstractValidator<ClientListQuery>
{
    public ClientListQueryValidator()
    {
        RuleFor(x => x.IncludeArchived).Must((q, include) => !(include && q.Archived == true))
            .WithMessage("Ask for archived clients only, or for them alongside the others, not both.");
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
        RuleFor(x => x.Domain).ValidBusiness().When(x => x.Domain is not null);
        RuleFor(x => x.Filter).IsInEnum().WithMessage("Filter must be active, owing or empty.");
        RuleFor(x => x.Sort).IsInEnum()
            .WithMessage("Sort must be name, projects, hours, activity, revenue, profit, margin or outstanding.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class CreateClientRequestValidator : AbstractValidator<CreateClientRequest>
{
    public CreateClientRequestValidator()
    {
        RuleFor(x => x.Name).ClientText("Name", 150, required: true);
        RuleFor(x => x.Organisation).ClientText("Organisation", 200);
        RuleFor(x => x.Label).ClientText("Label", 60, required: true);
        RuleFor(x => x.Note).ClientText("Note", 500);
        RuleFor(x => x.DomainCode).ValidBusiness().When(x => x.DomainCode is not null);
        RuleFor(x => x.Contacts).ValidContacts();
    }
}

public sealed class UpdateClientRequestValidator : AbstractValidator<UpdateClientRequest>
{
    public UpdateClientRequestValidator()
    {
        RuleFor(x => x.Name).ClientText("Name", 150, required: true);
        RuleFor(x => x.Organisation).ClientText("Organisation", 200);
        RuleFor(x => x.Label).ClientText("Label", 60, required: true);
        RuleFor(x => x.Note).ClientText("Note", 500);
        RuleFor(x => x.Contacts).ValidContacts();
    }
}

public sealed class ClientContactRequestValidator : AbstractValidator<ClientContactRequest>
{
    public ClientContactRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Contact kind must be email or phone.");
        RuleFor(x => x.Value).ClientText("Contact", 255, required: true);
        RuleFor(x => x.Value).Must(ValidationRules.IsPlainEmail).WithMessage("Enter a valid email address.").When(x => x.Kind == ContactKind.Email);
        RuleFor(x => x.Value)
            .Must(v => v.Count(char.IsAsciiDigit) is >= 6 and <= 20)
            .WithMessage("Enter a phone number with 6 to 20 digits.")
            .When(x => x.Kind == ContactKind.Phone && x.Value is not null);
        RuleFor(x => x.Label).ClientText("Contact label", 60);
        RuleFor(x => x.IsWhatsapp).Equal(false).When(x => x.Kind == ContactKind.Email)
            .WithMessage("Only phone numbers can be WhatsApp contacts.");
    }
}

internal static class ClientRuleExtensions
{
    public const int MaxContacts = 20;

    public static IRuleBuilderOptions<T, string?> ClientText<T>(this IRuleBuilder<T, string?> rule, string label, int max, bool required = false)
        => rule
            .Must(v => !required || !string.IsNullOrWhiteSpace(v)).WithMessage($"{label} is required.")
            .Must(v => v is null || v.Trim().Length <= max).WithMessage($"{label} must be at most {max} characters.");

    public static IRuleBuilderOptions<T, IEnumerable<ClientContactRequest>> ValidContacts<T>(
        this IRuleBuilderInitial<T, IReadOnlyList<ClientContactRequest>> rule)
        => rule
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Add at least one contact.")
            .Must(c => c.Count >= 1).WithMessage("Add at least one contact.")
            .Must(c => c.Count <= MaxContacts).WithMessage($"A client can have at most {MaxContacts} contacts.")
            .Must(c => c.Count(x => x.IsPrimary) <= 1).WithMessage("Only one contact can be primary.")
            .ForEach(c => c.SetValidator(new ClientContactRequestValidator()));
}

public sealed class CheckClientRequestValidator : AbstractValidator<CheckClientRequest>
{
    public CheckClientRequestValidator()
    {
        RuleFor(x => x.Name).ClientText("Name", 150);
        RuleFor(x => x.Contacts.Count).LessThanOrEqualTo(20).WithMessage("At most 20 contacts.");
        RuleForEach(x => x.Contacts).ChildRules(contact =>
        {
            contact.RuleFor(c => c.Kind).IsInEnum().WithMessage("Unknown contact kind.");
            contact.RuleFor(c => c.Value).MaximumLength(200).WithMessage("A contact is at most 200 characters.");
        });
    }
}
