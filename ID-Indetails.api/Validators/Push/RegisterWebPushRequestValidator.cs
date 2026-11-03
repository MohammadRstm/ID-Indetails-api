using System.Buffers.Text;
using FluentValidation;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.Services.Push.Implementation;

namespace ID_Indetails.api.Validators.Push;

public sealed class RegisterWebPushRequestValidator : AbstractValidator<RegisterWebPushRequest>
{
    public RegisterWebPushRequestValidator()
    {
        RuleFor(x => x.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The endpoint is required.")
            .MaximumLength(2048).WithMessage("The endpoint is too long.")
            .Must(WebPushEndpoints.IsAllowed).WithMessage("This isn't a browser's push service.");
        RuleFor(x => x.P256dh)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The p256dh key is required.")
            .MaximumLength(100).WithMessage("The p256dh key is too long.")
            .Must(WebPushCrypto.IsBrowserKey).WithMessage("The p256dh key isn't a P-256 public key.");
        RuleFor(x => x.Auth)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The auth secret is required.")
            .MaximumLength(50).WithMessage("The auth secret is too long.")
            .Must(IsSixteenBytes).WithMessage("The auth secret isn't 16 bytes of base64url.");
    }

    private static bool IsSixteenBytes(string value)
    {
        try
        {
            return Base64Url.DecodeFromChars(value).Length == 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
