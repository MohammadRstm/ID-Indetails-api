using FluentValidation;
using ID_Indetails.api.DTO.Request.Auth;

namespace ID_Indetails.api.Validators.Auth;

/// <summary>Only the size: a missing or unknown token is answered like the website's missing cookie (401 or 204).</summary>
public sealed class MobileRefreshTokenRequestValidator : AbstractValidator<MobileRefreshTokenRequest>
{
    public const int MaxLength = 200;

    public MobileRefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).MaximumLength(MaxLength).WithMessage("The refresh token is too long.");
    }
}
