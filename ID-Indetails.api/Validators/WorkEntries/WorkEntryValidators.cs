using FluentValidation;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.DTO.Request.WorkEntries;

namespace ID_Indetails.api.Validators.WorkEntries;

public sealed class SaveWorkEntryRequestValidator : AbstractValidator<SaveWorkEntryRequest>
{
    public const int MaxMoves = 20;

    public SaveWorkEntryRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0).WithMessage("Pick a project.");
        RuleFor(x => x.Description).MaximumLength(WorkEntryRules.MaxDescriptionLength)
            .WithMessage($"Description must be at most {WorkEntryRules.MaxDescriptionLength} characters.");
        RuleFor(x => x).EntryTimes(x => x.StartedAt, x => x.EndedAt).OverridePropertyName("endedAt");
        RuleFor(x => x.Moves).Must(m => m.Count <= MaxMoves).WithMessage($"At most {MaxMoves} entries can be moved at once.");
        RuleFor(x => x.Moves).Must(m => m.Select(x => x.Id).Distinct().Count() == m.Count).WithMessage("An entry can only be moved once.");
        RuleForEach(x => x.Moves).ChildRules(move =>
        {
            move.RuleFor(m => m.Id).GreaterThan(0).WithMessage("Entry id is required.");
            move.RuleFor(m => m).EntryTimes(m => m.StartedAt, m => m.EndedAt).OverridePropertyName("endedAt");
        });
    }
}

internal static class WorkEntryRuleExtensions
{
    /// <summary>Whole-minute times, so the stored duration matches exactly, 5 minutes to 16 hours apart.</summary>
    public static IRuleBuilderOptions<T, T> EntryTimes<T>(this IRuleBuilder<T, T> rule, Func<T, DateTime> start, Func<T, DateTime> end)
        => rule
            .Must(x => start(x) != default && end(x) != default).WithMessage("Start and end are required.")
            .Must(x => IsWholeMinute(start(x)) && IsWholeMinute(end(x))).WithMessage("Start and end must be whole minutes.")
            .Must(x => Minutes(start(x), end(x)) >= WorkEntryRules.MinMinutes)
            .WithMessage($"An entry needs to be at least {WorkEntryRules.MinMinutes} minutes.")
            .Must(x => Minutes(start(x), end(x)) <= WorkEntryRules.MaxMinutes)
            .WithMessage($"An entry can be at most {WorkEntryRules.MaxMinutes / 60} hours. Split longer work into several entries.");

    private static bool IsWholeMinute(DateTime value) => value.Second == 0 && value.Millisecond == 0 && value.Microsecond == 0;

    private static double Minutes(DateTime start, DateTime end)
        => (WorkEntryRules.AsUtc(end) - WorkEntryRules.AsUtc(start)).TotalMinutes;
}
