using System.Text.RegularExpressions;
using FluentValidation;
using ID_Indetails.api.DTO.Request.Support;

namespace ID_Indetails.api.Validators.Support;

public sealed partial class ReportBugRequestValidator : AbstractValidator<ReportBugRequest>
{
    public const int MaxTextLength = 4000;
    public const int MaxPageLength = 300;
    public const int MaxRecentErrors = 5;
    public const int MaxRefLength = 64;
    public const int MaxErrorMessageLength = 300;

    public ReportBugRequestValidator()
    {
        RuleFor(x => x.Description).Cascade(CascadeMode.Stop)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Say what you were trying to do, and what happened.")
            .MaximumLength(MaxTextLength).WithMessage($"This must be at most {MaxTextLength} characters.");
        RuleFor(x => x.Details)
            .MaximumLength(MaxTextLength).WithMessage($"This must be at most {MaxTextLength} characters.");
        RuleFor(x => x.Page)
            .MaximumLength(MaxPageLength).WithMessage($"The page must be at most {MaxPageLength} characters.")
            .Matches(PagePath()).When(x => x.Page is not null).WithMessage("The page must be a path of this app, such as /design/projects.");
        When(x => x.Error is not null, () =>
        {
            RuleFor(x => x.Error).Must(e => e!.Ref is not null || e.Message is not null).WithMessage("An error needs its reference or its message.");
            RuleFor(x => x.Error!.Ref).Matches(Reference()).WithMessage("A reference is the trace id of an error response.");
            RuleFor(x => x.Error!.At).Must(at => at != default).WithMessage("Say when the error happened.");
            RuleFor(x => x.Error!.Message)
                .MaximumLength(MaxErrorMessageLength).WithMessage($"The error's message must be at most {MaxErrorMessageLength} characters.")
                .Must(v => v is null || !v.Any(char.IsControl)).WithMessage("The error's message must be one line.");
        });
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

    /// <summary>A trace id: the request activity's (00-…-…-01), the server's own request id, or the start of one a person read out.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9\-:.]{1,64}$")]
    private static partial Regex Reference();

    /// <summary>A path of the app without its query or fragment, so no token in a link can be sent along.</summary>
    [GeneratedRegex(@"^/[A-Za-z0-9/_\-.~%]*$")]
    private static partial Regex PagePath();
}
