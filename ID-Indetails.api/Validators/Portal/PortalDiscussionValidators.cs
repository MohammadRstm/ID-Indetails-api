using FluentValidation;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;

namespace ID_Indetails.api.Validators.Portal;

internal static class DiscussionText
{
    public static IRuleBuilderOptions<T, string> Written<T>(this IRuleBuilder<T, string> rule, int max, string what)
        => rule
            .Must(b => !string.IsNullOrWhiteSpace(b)).WithMessage($"Write a {what}.")
            .Must(b => b.Trim().Length <= max).WithMessage($"A {what} can be at most {max} characters.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> MentionKeys<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule)
        => rule.Must(keys => keys.Count <= 50 && keys.All(k => k is { Length: > 1 and <= 24 })).WithMessage("Those mentions aren't valid.");
}

public sealed class PortalMessageRequestValidator : AbstractValidator<PortalMessageRequest>
{
    public PortalMessageRequestValidator()
    {
        RuleFor(x => x.Body).Written(PortalDiscussionRules.MaxMessageLength, "message");
        RuleFor(x => x.ClientToken).NotEmpty().MaximumLength(64).WithMessage("The message token is missing or invalid.");
        RuleFor(x => x.Mentions).MentionKeys();
    }
}

public sealed class PortalCommentRequestValidator : AbstractValidator<PortalCommentRequest>
{
    public PortalCommentRequestValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0).WithMessage("Choose a file.");
        RuleFor(x => x.Body).Written(PortalDiscussionRules.MaxCommentLength, "comment");
        RuleFor(x => x.ClientToken).NotEmpty().MaximumLength(64).WithMessage("The comment token is missing or invalid.");
        RuleFor(x => x.Mentions).MentionKeys();
    }
}

public sealed class PortalEditRequestValidator : AbstractValidator<PortalEditRequest>
{
    public PortalEditRequestValidator()
    {
        // the longer limit here; a comment's own is checked when it is saved
        RuleFor(x => x.Body).Written(PortalDiscussionRules.MaxMessageLength, "message");
        RuleFor(x => x.Mentions).MentionKeys();
    }
}

public sealed class PortalReadRequestValidator : AbstractValidator<PortalReadRequest>
{
    public PortalReadRequestValidator()
    {
        // a comment marker belongs to one file
        RuleFor(x => x).Must(x => (x.ItemId is null) == (x.LastCommentId is null)).WithMessage("Say which file's comments were read.");
        RuleFor(x => x.LastMessageId).GreaterThan(0).When(x => x.LastMessageId is not null);
        RuleFor(x => x.ItemId).GreaterThan(0).When(x => x.ItemId is not null);
        RuleFor(x => x.LastCommentId).GreaterThan(0).When(x => x.LastCommentId is not null);
    }
}

public sealed class PortalNotificationsReadRequestValidator : AbstractValidator<PortalNotificationsReadRequest>
{
    public PortalNotificationsReadRequestValidator()
    {
        RuleFor(x => x).Must(x => x.All || x.Ids is { Count: > 0 }).WithMessage("Say which notifications, or all.");
        RuleFor(x => x.Ids!).Must(ids => ids.Count <= 200).When(x => x.Ids is not null);
    }
}
