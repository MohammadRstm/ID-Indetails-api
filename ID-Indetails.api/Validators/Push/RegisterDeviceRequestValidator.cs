using System.Text.RegularExpressions;
using FluentValidation;
using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.Validators.Clients;

namespace ID_Indetails.api.Validators.Push;

public sealed partial class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.PushToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The push token is required.")
            .MaximumLength(255).WithMessage("The push token is too long.")
            .Must(t => ExpoToken().IsMatch(t)).WithMessage("This isn't an Expo push token.");
        RuleFor(x => x.Platform)
            .NotNull().WithMessage("The platform is required.")
            .IsInEnum().WithMessage("The platform must be ios or android.");
        RuleFor(x => x.AppVersion).ClientText("App version", 32);
    }

    [GeneratedRegex(@"^Expo(nent)?PushToken\[[^\]\s]+\]$")]
    private static partial Regex ExpoToken();
}
