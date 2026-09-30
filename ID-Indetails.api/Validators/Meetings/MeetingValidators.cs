using FluentValidation;
using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.Meetings;

public sealed class SaveMeetingRequestValidator : AbstractValidator<SaveMeetingRequest>
{
    public SaveMeetingRequestValidator()
    {
        RuleFor(x => x.StartsAt).NotEqual(default(DateTime)).WithMessage("Start is required.")
            .Must(IsWholeMinute).WithMessage("Start must be a whole minute.");
        RuleFor(x => x.EndsAt).NotEqual(default(DateTime)).WithMessage("End is required.")
            .Must(IsWholeMinute).WithMessage("End must be a whole minute.");
        RuleFor(x => x.Topic).MaximumLength(MeetingRules.MaxTopicLength)
            .WithMessage($"Topic must be at most {MeetingRules.MaxTopicLength} characters.");
        RuleFor(x => x.Description).MaximumLength(MeetingRules.MaxDescriptionLength)
            .WithMessage($"Description must be at most {MeetingRules.MaxDescriptionLength} characters.");
    }

    private static bool IsWholeMinute(DateTime value) => value.Second == 0 && value.Millisecond == 0 && value.Microsecond == 0;
}

public sealed class MeetingListQueryValidator : AbstractValidator<MeetingListQuery>
{
    public MeetingListQueryValidator()
    {
        RuleFor(x => x.Domain).ValidDomain().When(x => x.Domain is not null);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The range must end on or after its start.");
    }
}
