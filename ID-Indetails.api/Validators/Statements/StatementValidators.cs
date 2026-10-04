using FluentValidation;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Statements;

public sealed class CreateStatementRequestValidator : AbstractValidator<CreateStatementRequest>
{
    private const int MaxListItems = 5000;

    public CreateStatementRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum().WithMessage("Mode must be detail or total.");
        RuleFor(x => x.ExcludedMaterialIds).Must(ids => ids is null || ids.Count <= MaxListItems)
            .WithMessage($"At most {MaxListItems} materials can be left out.");
        RuleFor(x => x.UnlistedTypeKeys).Must(keys => keys is null || keys.Count <= MaxListItems)
            .WithMessage($"At most {MaxListItems} types can be left unlisted.");
        RuleForEach(x => x.UnlistedTypeKeys).MaximumLength(100).WithMessage("A material type is at most 100 characters.");
    }
}

public sealed class StatementListQueryValidator : AbstractValidator<StatementListQuery>
{
    public StatementListQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}
