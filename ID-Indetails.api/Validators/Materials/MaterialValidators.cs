using FluentValidation;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Materials;

public sealed class MaterialListQueryValidator : AbstractValidator<MaterialListQuery>
{
    public MaterialListQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The end date can't be before the start date.");
        RuleFor(x => x.Search).ClientText("Search", 200);
    }
}

/// <summary>Shape of a material; the columns are quantity (14,3), unit cost (18,4) and amount (18,2).</summary>
public sealed class MaterialEntryRequestValidator : AbstractValidator<MaterialEntryRequest>
{
    private const decimal MaxQuantity = 99_999_999_999.999m;
    private const decimal MaxUnitCost = 99_999_999_999_999.9999m;

    public MaterialEntryRequestValidator()
    {
        RuleFor(x => x.Name).ClientText("Type", 200, required: true);
        RuleFor(x => x.Quantity)
            .Must(v => v is null || (v > 0 && decimal.Round(v.Value, 3) == v)).WithMessage("Quantity must be more than zero, with at most 3 decimals.")
            .Must(v => v is null || v <= MaxQuantity).WithMessage("Quantity is too large.");
        RuleFor(x => x.Unit).ClientText("Unit", 20);
        RuleFor(x => x.UnitCost)
            .Must(v => v is null || (v > 0 && decimal.Round(v.Value, 4) == v)).WithMessage("Unit cost must be more than zero, with at most 4 decimals.")
            .Must(v => v is null || v <= MaxUnitCost).WithMessage("Unit cost is too large.")
            // the cost they give is stored as an amount
            .Must((x, cost) => x.Quantity is not > 0 || cost is not > 0 || x.Quantity.Value * cost.Value <= MoneyRules.MaxAmount)
            .WithMessage("Quantity × unit cost is too large.");
        RuleFor(x => x.Amount).Amount("Total", allowZero: false);
        RuleFor(x => x.Supplier).ClientText("Supplier", 200);
        RuleFor(x => x.Note).ClientText("Note", 500);
        RuleFor(x => x.Amount)
            .Must((x, amount) => (x.Quantity is > 0 && x.UnitCost is > 0) || amount is > 0)
            .WithMessage("Give the total, or a quantity and a unit cost.")
            // a total typed over quantity × unit cost sets the unit cost to total ÷ quantity
            .Must((x, amount) => x.Quantity is not > 0 || x.UnitCost is not > 0 || amount is not > 0 || amount.Value / x.Quantity.Value <= MaxUnitCost)
            .WithMessage("This total for that quantity makes the unit cost too large.");
    }
}

public sealed class NewMaterialRequestValidator : AbstractValidator<NewMaterialRequest>
{
    public NewMaterialRequestValidator()
    {
        Include(new MaterialEntryRequestValidator());
        RuleFor(x => x.ClientPrice).Amount("Client price");
        RuleFor(x => x.ClientRef).NotEqual(Guid.Empty).WithMessage("The client reference must be a UUID.");
    }
}

public sealed class AddMaterialsRequestValidator : AbstractValidator<AddMaterialsRequest>
{
    public const int MaxItems = 100;

    public AddMaterialsRequestValidator()
    {
        RuleFor(x => x.Items)
            .Must(items => items is { Count: >= 1 and <= MaxItems })
            .WithMessage($"Add 1 to {MaxItems} materials at a time.");
        RuleFor(x => x.Items)
            .Must(items => items is null || items.Where(i => i.ClientRef is not null).GroupBy(i => i.ClientRef).All(g => g.Count() == 1))
            .WithMessage("Each client reference can be used once in a batch.");
        RuleForEach(x => x.Items).SetValidator(new NewMaterialRequestValidator());
    }
}

public sealed class SetMaterialPriceRequestValidator : AbstractValidator<SetMaterialPriceRequest>
{
    public SetMaterialPriceRequestValidator()
    {
        RuleFor(x => x.ClientPrice).Amount("Client price");
    }
}
