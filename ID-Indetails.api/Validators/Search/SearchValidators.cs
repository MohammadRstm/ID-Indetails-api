using FluentValidation;
using ID_Indetails.api.DTO.Request.Search;

namespace ID_Indetails.api.Validators.Search;

public sealed class SearchQueryValidator : AbstractValidator<SearchQuery>
{
    public SearchQueryValidator()
    {
        RuleFor(x => x.Q).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
    }
}
