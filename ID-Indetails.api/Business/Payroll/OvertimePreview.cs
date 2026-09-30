using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// The overtime of work being logged or edited, among the person's other work that day (<see cref="Overtime"/>), and
/// where it falls: before the day's start, after its end (past midnight included), or past the hours the day asks for.
/// </summary>
public static class OvertimePreview
{
    /// <summary>Stands for the draft among the day's entries: no stored entry has it.</summary>
    public const long DraftId = 0;

    /// <param name="Hours">The day's working hours; null on a day off, when all of it is overtime.</param>
    public sealed record Result(int Minutes, int Overtime, int BeforeHours, int AfterHours, int PastRequired, WorkHours? Hours);

    /// <param name="draft">Its <see cref="Overtime.Work.Id"/> must be <see cref="DraftId"/>.</param>
    /// <param name="others">The person's other work that day, where it would be once the draft is saved.</param>
    public static Result Of(Overtime.Work draft, IEnumerable<Overtime.Work> others, PersonCalendar calendar)
    {
        var split = Overtime.Of(others.Where(w => w.Day == draft.Day).Append(draft), calendar).Single(s => s.Id == DraftId);
        var hours = calendar.HoursOn(draft.Day);
        if (hours is null)
        {
            return new Result(draft.Minutes, split.Overtime, 0, 0, 0, null);
        }

        var pastMidnight = draft.Start + draft.Minutes - draft.End;
        var before = Math.Max(0, Math.Min(draft.End, hours.Start) - draft.Start);
        var after = Math.Max(0, draft.End - Math.Max(draft.Start, hours.End)) + pastMidnight;
        return new Result(draft.Minutes, split.Overtime, before, after, Math.Max(0, split.Overtime - before - after), hours);
    }
}
