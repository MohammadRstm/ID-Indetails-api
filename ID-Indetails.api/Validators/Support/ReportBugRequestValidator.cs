using System.Text.RegularExpressions;
using FluentValidation;
using ID_Indetails.api.DTO.Request.Support;

namespace ID_Indetails.api.Validators.Support;

public sealed partial class ReportBugRequestValidator : AbstractValidator<ReportBugRequest>
{
    public const int MaxTitleLength = 120;
    public const int MaxTextLength = 4000;
    public const int MaxPageLength = 300;
    public const int MaxRecentErrors = 5;
    public const int MaxRefLength = 64;

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
        RuleFor(x => x.RecentErrors).Must(e => e.Count <= MaxRecentErrors).WithMessage($"At most {MaxRecentErrors} recent errors.");
        RuleForEach(x => x.RecentErrors).ChildRules(error =>
        {
            error.RuleFor(e => e.Ref).Matches(Reference()).WithMessage("A reference is the trace id of an error response.");
            error.RuleFor(e => e.Status).InclusiveBetween(400, 599).WithMessage("A status is an error's, 400 to 599.");
            error.RuleFor(e => e.Path)
                .MaximumLength(MaxPageLength).WithMessage($"The path must be at most {MaxPageLength} characters.")
                .Matches(PagePath()).When(e => e.Path is not null).WithMessage("The path must be one without its query.");
        });
    }

    /// <summary>A trace id: the request activity's (00-…-…-01) or the server's own request id.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9\-:.]{1,64}$")]
    private static partial Regex Reference();

    /// <summary>A path of the app without its query or fragment, so no token in a link can be sent along.</summary>
    [GeneratedRegex(@"^/[A-Za-z0-9/_\-.~%]*$")]
    private static partial Regex PagePath();
}
