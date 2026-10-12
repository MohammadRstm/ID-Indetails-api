using FluentValidation;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.DTO.Request.Notifications;

namespace ID_Indetails.api.Validators.Notifications;

public sealed class SaveNotificationSettingsRequestValidator : AbstractValidator<SaveNotificationSettingsRequest>
{
    public SaveNotificationSettingsRequestValidator()
    {
        RuleFor(x => x.QuietToMinute)
            .Must((x, to) => (x.QuietFromMinute is null) == (to is null))
            .WithMessage("Set both ends of the quiet hours, or neither.");
        RuleFor(x => x.QuietFromMinute)
            .InclusiveBetween(0, QuietHours.LastMinute).When(x => x.QuietFromMinute is not null)
            .WithMessage("The quiet hours start at a minute of the day (0 to 1439).");
        RuleFor(x => x.QuietToMinute)
            .InclusiveBetween(0, QuietHours.LastMinute).When(x => x.QuietToMinute is not null)
            .WithMessage("The quiet hours end at a minute of the day (0 to 1439).")
            .NotEqual(x => x.QuietFromMinute).When(x => x.QuietFromMinute is not null)
            .WithMessage("The quiet hours must start and end at different times.");
        RuleFor(x => x.MutedCategories)
            .NotNull().WithMessage("Muted categories are required.")
            .Must(c => c is null || c.Distinct().Count() == c.Count).WithMessage("Each category can be muted once.");
        RuleForEach(x => x.MutedCategories).IsInEnum().WithMessage("Unknown category.");
    }
}
