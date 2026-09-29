using FluentValidation;
using ID_Indetails.api.DTO.Request.Finance;

namespace ID_Indetails.api.Validators.Finance;

public sealed class FinancePeriodQueryValidator : AbstractValidator<FinancePeriodQuery>
{
    public FinancePeriodQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The period must end on or after its start.");
    }
}

public sealed class FinanceMonthsQueryValidator : AbstractValidator<FinanceMonthsQuery>
{
    public const int MaxMonths = 36;

    public FinanceMonthsQueryValidator()
    {
        RuleFor(x => x.Months).InclusiveBetween(1, MaxMonths).WithMessage($"Months must be between 1 and {MaxMonths}.");
    }
}
