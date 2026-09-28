using FluentValidation;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Labour;

internal static class LabourRuleExtensions
{
    public const int MaxWorkers = 10_000;

    public static IRuleBuilderOptions<T, int> Workers<T>(this IRuleBuilder<T, int> rule)
        => rule.InclusiveBetween(0, MaxWorkers).WithMessage($"Workers must be between 0 and {MaxWorkers}.");
}

public sealed class SubmitLabourWeekRequestValidator : AbstractValidator<SubmitLabourWeekRequest>
{
    public SubmitLabourWeekRequestValidator()
    {
        RuleFor(x => x.Days)
            .Must(days => days is { Count: >= 1 and <= LabourRules.DaysPerWeek })
            .WithMessage($"Report 1 to {LabourRules.DaysPerWeek} days, Monday to Saturday.");
        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.RuleFor(d => d.Workers).Workers();
            day.RuleFor(d => d.DailyPay).Amount("Daily pay");
        });
        RuleFor(x => x.Note).ClientText("Note", 500);
    }
}

public sealed class SetLabourDefaultsRequestValidator : AbstractValidator<SetLabourDefaultsRequest>
{
    public SetLabourDefaultsRequestValidator()
    {
        RuleFor(x => x.Workers).Workers();
        RuleFor(x => x.DailyPay).Amount("Daily pay");
    }
}
