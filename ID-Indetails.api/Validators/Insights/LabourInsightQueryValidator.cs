using FluentValidation;
using ID_Indetails.api.DTO.Request.Insights;

namespace ID_Indetails.api.Validators.Insights;

public sealed class LabourInsightQueryValidator : AbstractValidator<LabourInsightQuery>
{
    public LabourInsightQueryValidator()
    {
        RuleFor(x => x.Months).InclusiveBetween(1, ExpenseTrendsQueryValidator.MaxMonths).When(x => x.Months is not null);
    }
}
