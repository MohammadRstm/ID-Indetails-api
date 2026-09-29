using FluentValidation;
using ID_Indetails.api.Business.Messaging;
using ID_Indetails.api.DTO.Request.Messaging;

namespace ID_Indetails.api.Validators.Messaging;

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Body).Must(b => !string.IsNullOrWhiteSpace(b)).WithMessage("Write a message.");
        RuleFor(x => x.Body).MaximumLength(MessagingRules.MaxBodyLength)
            .WithMessage($"A message is at most {MessagingRules.MaxBodyLength} characters.");
        RuleFor(x => x.MentionIds).Must(ids => ids.Count <= MessagingRules.MaxMentions)
            .WithMessage($"Mention at most {MessagingRules.MaxMentions} people.");
        RuleForEach(x => x.MentionIds).GreaterThan(0).WithMessage("Mentions must be people.");
    }
}

public sealed class MessagePageQueryValidator : AbstractValidator<MessagePageQuery>
{
    public const int MaxLimit = 100;

    public MessagePageQueryValidator()
    {
        RuleFor(x => x.Before).GreaterThan(0).When(x => x.Before is not null).WithMessage("Before must be a message.");
        RuleFor(x => x.Limit).InclusiveBetween(1, MaxLimit).WithMessage($"Limit must be between 1 and {MaxLimit}.");
    }
}
