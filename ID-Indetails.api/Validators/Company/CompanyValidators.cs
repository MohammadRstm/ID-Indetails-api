using FluentValidation;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.Validators.Clients;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Company;

public sealed class UpdateCompanySettingsRequestValidator : AbstractValidator<UpdateCompanySettingsRequest>
{
    public UpdateCompanySettingsRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Company name is required.")
            .MaximumLength(150).WithMessage("Company name must be at most 150 characters.");
        RuleFor(x => x.Address).ClientText("Address", 300);
        RuleFor(x => x.Website)
            .Must(w => string.IsNullOrWhiteSpace(w) || (w.Trim().Length <= 200 && IsWebAddress(w.Trim())))
            .WithMessage("Website must be a web address such as indetails.com, at most 200 characters.");
        RuleFor(x => x.WeekStart).Must(d => d is 0 or 1).WithMessage("The week starts on Sunday (0) or Monday (1).");
        RuleFor(x => x.DefaultCurrency).Currency();
    }

    /// <summary>A host with a dot (indetails.com), or an http(s) address.</summary>
    private static bool IsWebAddress(string value)
        => !value.Any(char.IsWhiteSpace)
            && Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.Host.Contains('.');
}

public sealed class CompanyContactEmailRequestValidator : AbstractValidator<CompanyContactEmailRequest>
{
    public CompanyContactEmailRequestValidator()
    {
        RuleFor(x => x.Email!).ValidEmail().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class SetWorkingTimeRequestValidator : AbstractValidator<SetWorkingTimeRequest>
{
    public SetWorkingTimeRequestValidator()
    {
        RuleFor(x => x.WorkDays).WorkDays();
        RuleFor(x => x).Hours(x => x.StartMinute, x => x.EndMinute).OverridePropertyName("endMinute");
        RuleFor(x => x.BreakMinutes)
            .InclusiveBetween(0, 240).WithMessage("The break must be between 0 and 240 minutes.")
            .Must((x, b) => b < x.EndMinute - x.StartMinute).WithMessage("The break must be shorter than the working day.");
        RuleFor(x => x.OvertimeMultiplier)
            .InclusiveBetween(1m, 5m).WithMessage("The overtime multiplier must be between 1 and 5.")
            .Must(m => decimal.Round(m, 2) == m).WithMessage("The overtime multiplier can have at most 2 decimals.");
    }
}

public sealed class SetScheduleRequestValidator : AbstractValidator<SetScheduleRequest>
{
    public SetScheduleRequestValidator()
    {
        RuleFor(x => x.Days).Empty().When(x => x.FollowsDefault).WithMessage("Leave the days empty when following the company week.");
        RuleFor(x => (IReadOnlyList<int>)x.Days.Select(d => d.Weekday).ToList())
            .WorkDays().When(x => !x.FollowsDefault).OverridePropertyName("days");
        RuleForEach(x => x.Days).ChildRules(day => day.RuleFor(d => d).Hours(d => d.StartMinute, d => d.EndMinute).OverridePropertyName("endMinute"));
    }
}

public sealed class ScheduleListQueryValidator : AbstractValidator<ScheduleListQuery>
{
    public ScheduleListQueryValidator()
    {
        RuleFor(x => x.Domain)
            .Must(d => d is null || DomainCodes.All.Contains(d))
            .WithMessage($"Domain must be one of: {string.Join(", ", DomainCodes.All)}.");
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search must be at most 100 characters.");
    }
}

public sealed class CreateOverheadRequestValidator : AbstractValidator<CreateOverheadRequest>
{
    public CreateOverheadRequestValidator()
    {
        RuleFor(x => x.Name).OverheadName();
        RuleFor(x => x.GroupId).GreaterThan(0).WithMessage("Choose the category.");
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
    }
}

public sealed class UpdateOverheadRequestValidator : AbstractValidator<UpdateOverheadRequest>
{
    public UpdateOverheadRequestValidator()
    {
        RuleFor(x => x.Name).OverheadName();
        RuleFor(x => x.GroupId).GreaterThan(0).WithMessage("Choose the category.");
    }
}

public sealed class OverheadGroupRequestValidator : AbstractValidator<OverheadGroupRequest>
{
    public OverheadGroupRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name the category.")
            .MaximumLength(OverheadRules.MaxGroupName).WithMessage($"A category's name is at most {OverheadRules.MaxGroupName} characters.");
    }
}

public sealed class ChangeOverheadAmountRequestValidator : AbstractValidator<ChangeOverheadAmountRequest>
{
    public ChangeOverheadAmountRequestValidator()
    {
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
    }
}

public sealed class ChangeOverheadCurrencyRequestValidator : AbstractValidator<ChangeOverheadCurrencyRequest>
{
    public ChangeOverheadCurrencyRequestValidator()
    {
        RuleFor(x => x.Currency).Currency();
        RuleFor(x => x.Amount).Amount("Amount", allowZero: false);
    }
}

internal static class CompanyRuleExtensions
{
    public const int MinSpanMinutes = 30;

    public static IRuleBuilderOptions<T, IReadOnlyList<int>> WorkDays<T>(this IRuleBuilder<T, IReadOnlyList<int>> rule)
        => rule
            .NotEmpty().WithMessage("Choose at least one working day.")
            .Must(days => days.All(d => d is >= 0 and <= 6)).WithMessage("Weekdays run from 0 (Sunday) to 6 (Saturday).")
            .Must(days => days.Distinct().Count() == days.Count).WithMessage("Each weekday may appear only once.");

    public static IRuleBuilderOptions<T, T> Hours<T>(this IRuleBuilder<T, T> rule, Func<T, int> start, Func<T, int> end)
        => rule.Must(x => start(x) >= 0 && end(x) <= 1440 && end(x) - start(x) >= MinSpanMinutes)
            .WithMessage($"Hours must fall within the day, and end at least {MinSpanMinutes} minutes after they start.");

    public static IRuleBuilderOptions<T, string> OverheadName<T>(this IRuleBuilder<T, string> rule)
        => rule
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(150).WithMessage("Name must be at most 150 characters.");
}

public sealed class AccountLabelRequestValidator : AbstractValidator<AccountLabelRequest>
{
    public AccountLabelRequestValidator()
    {
        RuleFor(x => x.Name).ClientText("Name", AccountLabelRules.MaxName, required: true);
    }
}

public sealed class AccountLabelOrderRequestValidator : AbstractValidator<AccountLabelOrderRequest>
{
    public AccountLabelOrderRequestValidator()
    {
        RuleFor(x => x.Ids).NotNull().WithMessage("List the labels in their new order.");
    }
}
