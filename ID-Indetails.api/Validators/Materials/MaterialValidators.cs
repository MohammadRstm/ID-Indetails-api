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
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or more.");
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
            .Must(v => v is null || (v > 0 && v <= MaxQuantity && decimal.Round(v.Value, 3) == v))
            .WithMessage("Quantity must be more than zero, with at most 3 decimals.");
        RuleFor(x => x.Unit).ClientText("Unit", 20);
        RuleFor(x => x.UnitCost)
            .Must(v => v is null || (v > 0 && v <= MaxUnitCost && decimal.Round(v.Value, 4) == v))
            .WithMessage("Unit cost must be more than zero, with at most 4 decimals.");
        RuleFor(x => x.Amount).Amount("Total", allowZero: false);
        RuleFor(x => x.Supplier).ClientText("Supplier", 200);
        RuleFor(x => x.Note).ClientText("Note", 500);
        RuleFor(x => x.Amount)
            .Must((x, amount) => (x.Quantity is > 0 && x.UnitCost is > 0) || amount is > 0)
            .WithMessage("Give the total, or a quantity and a unit cost.");
    }
}

public sealed class NewMaterialRequestValidator : AbstractValidator<NewMaterialRequest>
{
    public NewMaterialRequestValidator()
    {
        Include(new MaterialEntryRequestValidator());
        RuleFor(x => x.ClientPrice).Amount("Client price");
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
