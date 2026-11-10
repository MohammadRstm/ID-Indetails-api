using FluentValidation;
using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.Validators.Projects;

namespace ID_Indetails.api.Validators.Insights;

public sealed class ProjectScenarioQueryValidator : AbstractValidator<ProjectScenarioQuery>
{
    public const decimal MaxAmount = 100_000_000m;
    public const int MaxDelayWeeks = 104;

    public ProjectScenarioQueryValidator()
    {
        RuleFor(x => x)
            .Must(q => q.VoAmount is not null || q.ExtraCost is not null || q.SupervisionRate is not null || q.TvaRate is not null
                || q.SettleOwed is not null || q.DelayWeeks is not null)
            .WithName("scenario")
            .WithMessage("Choose at least one change to simulate.");
        RuleFor(x => x.VoAmount).NotEqual(0).InclusiveBetween(-MaxAmount, MaxAmount).When(x => x.VoAmount is not null);
        RuleFor(x => x.VoAmount).Null().When(x => x.SettleOwed is not null)
            .WithMessage("A settlement sets the price to what the client paid, so a variation order would change nothing: simulate them apart.");
        RuleFor(x => x.ExtraCost).GreaterThan(0).LessThanOrEqualTo(MaxAmount).When(x => x.ExtraCost is not null);
        RuleFor(x => x.ExtraCostSource).NotNull().When(x => x.ExtraCost is not null)
            .WithMessage("Say where the extra cost would go: labour, expenses, site_expense or ground_labour.");
        RuleFor(x => x.ExtraCost).NotNull().When(x => x.ExtraCostSource is not null)
            .WithMessage("Give the extra cost's amount.");
        RuleFor(x => x.SupervisionRate).SupervisionRate();
        RuleFor(x => x.TvaRate).TvaRate();
        RuleFor(x => x.DelayWeeks).InclusiveBetween(1, MaxDelayWeeks).When(x => x.DelayWeeks is not null);
    }
}

public sealed class ExpenseTrendsQueryValidator : AbstractValidator<ExpenseTrendsQuery>
{
    public const int DefaultMonths = 12;
    public const int MaxMonths = 24;

    public ExpenseTrendsQueryValidator()
    {
        RuleFor(x => x.Months).InclusiveBetween(1, MaxMonths).When(x => x.Months is not null);
    }
}
