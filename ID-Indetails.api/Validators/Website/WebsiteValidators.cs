using FluentValidation;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.Validators.Common;

namespace ID_Indetails.api.Validators.Website;

internal static class WebsiteText
{
    /// <summary>Text the website must show: written, and not longer than <paramref name="max"/>.</summary>
    public static IRuleBuilderOptions<T, string> Required<T>(this IRuleBuilder<T, string> rule, string label, int max)
        => rule
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage($"{label} is required.")
            .Must(v => v is null || v.Trim().Length <= max).WithMessage($"{label} can be at most {max} characters.");

    public static IRuleBuilderOptions<T, string?> Optional<T>(this IRuleBuilder<T, string?> rule, string label, int max)
        => rule.Must(v => v is null || v.Trim().Length <= max).WithMessage($"{label} can be at most {max} characters.");

    public static IRuleBuilderOptions<T, string> SecureLink<T>(this IRuleBuilder<T, string> rule, string label)
        => rule
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage($"{label} is required.")
            .Must(v => v is null || v.Trim().Length <= WebsiteRules.MaxUrlLength).WithMessage($"{label} can be at most {WebsiteRules.MaxUrlLength} characters.")
            .Must(v => v is null || IsSecureLink(v.Trim())).WithMessage($"{label} must be a full https:// address.");

    public static IRuleBuilderOptions<T, decimal> Latitude<T>(this IRuleBuilder<T, decimal> rule)
        => rule.InclusiveBetween(-90, 90).WithMessage("The latitude must be between -90 and 90.");

    public static IRuleBuilderOptions<T, decimal> Longitude<T>(this IRuleBuilder<T, decimal> rule)
        => rule.InclusiveBetween(-180, 180).WithMessage("The longitude must be between -180 and 180.");

    private static bool IsSecureLink(string value)
        => !value.Any(char.IsWhiteSpace) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Contains('.');
}

public sealed class WebsiteProjectRequestValidator : AbstractValidator<WebsiteProjectRequest>
{
    public WebsiteProjectRequestValidator()
    {
        RuleFor(x => x.Title)
            .Required("The title", WebsiteRules.MaxTitleLength)
            .Must(t => t is null || WebsiteRules.SlugOf(t).Length > 0)
            .WithMessage("The name needs at least one Latin letter or digit: the project's page address is made from it.");
        RuleFor(x => x.Type)
            .Required("The type", WebsiteRules.MaxTypeLength)
            .Must(t => t is null || WebsiteRules.TypeKey(t).Length > 0)
            .WithMessage("The type needs at least one Latin letter or digit.");
        RuleFor(x => x.Status).IsInEnum().WithMessage("Choose in progress or completed.");
        RuleFor(x => x.Status).NotNull().When(x => x.ProjectId is null).WithMessage("Choose a status, or link a system project to follow its status.");
        RuleFor(x => x.Year).InclusiveBetween(1900, 2100).WithMessage("The year must be between 1900 and 2100.");
        RuleFor(x => x.Year).NotNull().When(x => x.ProjectId is null).WithMessage("Enter a year, or link a system project to follow its year.");
        RuleFor(x => x.Client).Optional("The client", WebsiteRules.MaxClientLength);
        RuleFor(x => x.City).Optional("The city", WebsiteRules.MaxPlaceLength);
        RuleFor(x => x.Country).Optional("The country", WebsiteRules.MaxPlaceLength);
        RuleFor(x => x.Description).Optional("The description", WebsiteRules.MaxDescriptionLength);
        RuleFor(x => x.ConceptDescription).Optional("The concept description", WebsiteRules.MaxDescriptionLength);
        RuleFor(x => x.Latitude!.Value).Latitude().OverridePropertyName("latitude").When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude!.Value).Longitude().OverridePropertyName("longitude").When(x => x.Longitude is not null);
        RuleFor(x => x.Longitude)
            .Must((x, lng) => (x.Latitude is null) == (lng is null))
            .WithMessage("Give both the latitude and the longitude, or neither.");
        RuleFor(x => x.ProjectId).GreaterThan(0).When(x => x.ProjectId is not null).WithMessage("That project doesn't exist.");
        RuleFor(x => x.HeroAlt).Optional("The main picture's description", WebsiteRules.MaxAltLength);
    }
}

public sealed class WebsiteImageRequestValidator : AbstractValidator<WebsiteImageRequest>
{
    public WebsiteImageRequestValidator()
    {
        RuleFor(x => x.FileId).GreaterThan(0).WithMessage("Upload a picture first.");
        RuleFor(x => x.Alt).Required("The picture's description", WebsiteRules.MaxAltLength);
    }
}

public sealed class WebsiteImageEditRequestValidator : AbstractValidator<WebsiteImageEditRequest>
{
    public WebsiteImageEditRequestValidator()
    {
        RuleFor(x => x.Alt).Required("The picture's description", WebsiteRules.MaxAltLength);
    }
}

public sealed class WebsiteOrderRequestValidator : AbstractValidator<WebsiteOrderRequest>
{
    public WebsiteOrderRequestValidator()
    {
        RuleFor(x => x.Ids).NotNull().Must(ids => ids.Count <= 500).WithMessage("That order isn't valid.");
    }
}

public sealed class WebsiteConveyorRequestValidator : AbstractValidator<WebsiteConveyorRequest>
{
    public WebsiteConveyorRequestValidator()
    {
        RuleFor(x => x.ProjectIds)
            .NotNull()
            .Must(ids => ids.Count <= WebsiteRules.MaxConveyor).WithMessage($"The moving strip shows at most {WebsiteRules.MaxConveyor} projects.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("A project can only be in the moving strip once.");
    }
}

public sealed class WebsiteServiceRequestValidator : AbstractValidator<WebsiteServiceRequest>
{
    public WebsiteServiceRequestValidator()
    {
        RuleFor(x => x.Title).Required("The title", WebsiteRules.MaxServiceTitleLength);
        RuleFor(x => x.Description).Required("The description", WebsiteRules.MaxServiceDescriptionLength);
    }
}

public sealed class WebsiteLocationRequestValidator : AbstractValidator<WebsiteLocationRequest>
{
    public WebsiteLocationRequestValidator()
    {
        RuleFor(x => x.City).Optional("The city", WebsiteRules.MaxPlaceLength);
        RuleFor(x => x.Country).Optional("The country", WebsiteRules.MaxPlaceLength);
        RuleFor(x => x.Latitude!.Value).Latitude().OverridePropertyName("latitude").When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude!.Value).Longitude().OverridePropertyName("longitude").When(x => x.Longitude is not null);
        RuleFor(x => x.Longitude)
            .Must((x, lng) => (x.Latitude is null) == (lng is null))
            .WithMessage("Give both the latitude and the longitude, or neither.");
        RuleFor(x => x.City)
            .Must((x, city) => x.Latitude is not null || (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(x.Country)))
            .WithMessage("Give the office's coordinates, or its city and country.");
        RuleFor(x => x.Address).Required("The address", WebsiteRules.MaxAddressLength);
        RuleFor(x => x.Phone).Required("The phone number", WebsiteRules.MaxPhoneLength);
    }
}

public sealed class WebsiteSocialLinkRequestValidator : AbstractValidator<WebsiteSocialLinkRequest>
{
    public WebsiteSocialLinkRequestValidator()
    {
        RuleFor(x => x.Label).Required("The name", WebsiteRules.MaxLabelLength);
        RuleFor(x => x.Url).SecureLink("The link");
    }
}

/// <summary>The same rules the website's form checks, checked again here.</summary>
public sealed class WebsiteContactMessageRequestValidator : AbstractValidator<WebsiteContactMessageRequest>
{
    public WebsiteContactMessageRequestValidator()
    {
        RuleFor(x => x.Name)
            .Required("Your name", WebsiteContactRules.MaxNameLength)
            .Must(n => n is null || !n.Any(char.IsControl)).WithMessage("Your name can't contain line breaks or control characters.");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Phone)
            .Must(p => WebsiteContactRules.IsPhone(p!.Trim()))
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Enter a phone number of at least 6 digits, using only digits, spaces, ( ) . - and a leading +.");
        RuleFor(x => x.Message)
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("Write a message.")
            .Must(m => m is null || m.Trim().Length is >= WebsiteContactRules.MinMessageLength and <= WebsiteContactRules.MaxMessageLength)
            .WithMessage($"A message must be {WebsiteContactRules.MinMessageLength} to {WebsiteContactRules.MaxMessageLength} characters.");
    }
}

public sealed class WebsiteAnalyticsQueryValidator : AbstractValidator<WebsiteAnalyticsQuery>
{
    public WebsiteAnalyticsQueryValidator()
    {
        RuleFor(x => x.To).NotNull().When(x => x.From is not null).WithMessage("Choose the last day of the period.");
        RuleFor(x => x.From).NotNull().When(x => x.To is not null).WithMessage("Choose the first day of the period.");
        RuleFor(x => x.From).GreaterThanOrEqualTo(WebsiteAnalyticsRules.FirstDay).When(x => x.From is not null)
            .WithMessage("Google Analytics has no data before 14 August 2015.");
        RuleFor(x => x.To).Must((q, to) => q.From is null || to is null || to >= q.From)
            .WithMessage("The period must end on or after the day it starts.");
        RuleFor(x => x.To).Must((q, to) => q.From is null || to is null || to.Value.DayNumber - q.From.Value.DayNumber < WebsiteAnalyticsRules.MaxDays)
            .WithMessage($"A period covers at most {WebsiteAnalyticsRules.MaxDays} days.");
    }
}
