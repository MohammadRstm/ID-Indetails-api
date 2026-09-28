using FluentValidation;
using ID_Indetails.api.DTO.Request.Settings;

namespace ID_Indetails.api.Validators.Settings;

public sealed class SaveDomainSettingsRequestValidator : AbstractValidator<SaveDomainSettingsRequest>
{
    public SaveDomainSettingsRequestValidator()
    {
        RuleFor(x => x.EditWindow).IsInEnum().WithMessage("Edit window must be none, one_day, seven_days or unlimited.");
        RuleFor(x => x.NotifyEntry).IsInEnum().WithMessage("Entry notifications must be all, long, digest or off.");
    }
}
