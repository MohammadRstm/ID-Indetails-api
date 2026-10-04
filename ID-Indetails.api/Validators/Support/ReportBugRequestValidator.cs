using System.Text.RegularExpressions;
using FluentValidation;
using ID_Indetails.api.DTO.Request.Support;

namespace ID_Indetails.api.Validators.Support;

public sealed partial class ReportBugRequestValidator : AbstractValidator<ReportBugRequest>
{
    public const int MaxTitleLength = 120;
    public const int MaxTextLength = 4000;
    public const int MaxPageLength = 300;

    public ReportBugRequestValidator()
    {
        RuleFor(x => x.Title).Cascade(CascadeMode.Stop)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Say in a few words what went wrong.")
            .MaximumLength(MaxTitleLength).WithMessage($"The title must be at most {MaxTitleLength} characters.")
            .Must(v => !v.Any(char.IsControl)).WithMessage("The title must be one line.");
        RuleFor(x => x.Description).Cascade(CascadeMode.Stop)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Describe what happened.")
            .MaximumLength(MaxTextLength).WithMessage($"The description must be at most {MaxTextLength} characters.");
        RuleFor(x => x.Steps)
            .MaximumLength(MaxTextLength).WithMessage($"The steps must be at most {MaxTextLength} characters.");
        RuleFor(x => x.Page)
            .MaximumLength(MaxPageLength).WithMessage($"The page must be at most {MaxPageLength} characters.")
            .Matches(PagePath()).When(x => x.Page is not null).WithMessage("The page must be a path of this app, such as /design/projects.");
    }

    /// <summary>A path of the app without its query or fragment, so no token in a link can be sent along.</summary>
    [GeneratedRegex(@"^/[A-Za-z0-9/_\-.~%]*$")]
    private static partial Regex PagePath();
}
