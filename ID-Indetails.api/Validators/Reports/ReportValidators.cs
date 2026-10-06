using FluentValidation;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Validators.Reports;

public sealed class WorkReportQueryValidator : AbstractValidator<WorkReportQuery>
{
    public const int MaxDays = 3 * 366;

    public WorkReportQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The range must end on or after its start.");
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber <= MaxDays)
            .WithMessage("A report covers at most three years.").OverridePropertyName("to");
        RuleFor(x => x.Internal).Must((q, isInternal) => !(isInternal && q.ClientId is not null))
            .WithMessage("Choose a client or internal work, not both.");
    }
}

public sealed class OverviewQueryValidator : AbstractValidator<OverviewQuery>
{
    public OverviewQueryValidator()
    {
        RuleFor(x => x.Period).IsInEnum().WithMessage("Period must be quarter, month, last30 or custom.");
        When(x => x.Period == OverviewPeriod.Custom, () =>
        {
            RuleFor(x => x.From).NotNull().WithMessage("Choose the first day of the period.");
            RuleFor(x => x.To).NotNull().WithMessage("Choose the last day of the period.");
            RuleFor(x => x.To).Must((q, to) => q.From is null || to is null || to >= q.From)
                .WithMessage("The period must end on or after the day it starts.");
            RuleFor(x => x.To).Must((q, to) => q.From is null || to is null || to.Value.DayNumber - q.From.Value.DayNumber < 3 * 366)
                .WithMessage("A period covers at most three years.");
        });
        RuleFor(x => x.From).Null().When(x => x.Period != OverviewPeriod.Custom).WithMessage("Only a custom period takes dates.");
        RuleFor(x => x.To).Null().When(x => x.Period != OverviewPeriod.Custom).WithMessage("Only a custom period takes dates.");
    }
}
