using FluentValidation;
using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.DTO.Request.TimeOff;
using ID_Indetails.api.Validators.Common;
using ID_Indetails.api.Validators.Employees;

namespace ID_Indetails.api.Validators.TimeOff;

public sealed class AskTimeOffRequestValidator : AbstractValidator<AskTimeOffRequest>
{
    /// <summary>A guard against a typo in the year, not a policy: time off has no allowance.</summary>
    public const int MaxDays = 366;

    public AskTimeOffRequestValidator()
    {
        RuleFor(x => x.FirstDay).NotEqual(default(DateOnly)).WithMessage("Choose the first day.");
        RuleFor(x => x.LastDay).NotEqual(default(DateOnly)).WithMessage("Choose the last day.");
        RuleFor(x => x.LastDay)
            .GreaterThanOrEqualTo(x => x.FirstDay).WithMessage("The last day can't be before the first.")
            .LessThan(x => x.FirstDay.AddDays(MaxDays)).WithMessage("Time off can be at most a year long.")
            .When(x => x.FirstDay != default && x.LastDay != default);
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Say why you need the time off.")
            .MaximumLength(TimeOffRules.MaxNoteLength).WithMessage($"The reason must be at most {TimeOffRules.MaxNoteLength} characters.");
    }
}

public sealed class ApproveTimeOffRequestValidator : AbstractValidator<ApproveTimeOffRequest>
{
    public ApproveTimeOffRequestValidator()
        => RuleFor(x => x.Note).MaximumLength(TimeOffRules.MaxNoteLength).WithMessage($"The note must be at most {TimeOffRules.MaxNoteLength} characters.");
}

public sealed class TimeOffNoteRequestValidator : AbstractValidator<TimeOffNoteRequest>
{
    public TimeOffNoteRequestValidator()
        => RuleFor(x => x.Note).MaximumLength(TimeOffRules.MaxNoteLength).WithMessage($"The note must be at most {TimeOffRules.MaxNoteLength} characters.");
}

public sealed class TimeOffListQueryValidator : AbstractValidator<TimeOffListQuery>
{
    public TimeOffListQueryValidator()
    {
        RuleFor(x => x.View).IsInEnum().WithMessage("View must be pending, current or past.");
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).InclusiveBetween(1, EmployeeListQueryValidator.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {EmployeeListQueryValidator.MaxPageSize}.");
    }
}

public sealed class AwayQueryValidator : AbstractValidator<AwayQuery>
{
    public const int MaxDays = 92;

    public AwayQueryValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value).WithMessage("The end can't be before the start.")
            .LessThan(x => x.From!.Value.AddDays(MaxDays)).WithMessage($"Ask for at most {MaxDays} days at a time.")
            .When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.From).NotNull().When(x => x.To is not null).WithMessage("Give the start too.");
    }
}
