namespace ID_Indetails.api.Business.WorkingTime;

/// <summary>A day's working hours; <see cref="Required"/> is the span minus the paid break.</summary>
public sealed record WorkHours(int Start, int End, int Break)
{
    public int Required => WorkWeek.RequiredMinutes(Start, End, Break);

    /// <summary>The part of the span not required: the paid break (0 on days of 6 hours or less).</summary>
    public int PaidBreak => End - Start - Required;

    public int Span => End - Start;
}

/// <summary>
/// One person's working days over time: their own schedule where they have one, otherwise the
/// company week, each taken from the version in force on the day. The break and the overtime
/// multiplier always come from the company policy.
/// </summary>
public sealed class PersonCalendar
{
    public sealed record Policy(DateOnly EffectiveFrom, byte WorkDays, int Start, int End, int Break, decimal OvertimeMultiplier);

    public sealed record Schedule(DateOnly EffectiveFrom, bool FollowsDefault, IReadOnlyDictionary<int, (int Start, int End)> Days);

    private readonly List<Policy> _policies;
    private readonly List<Schedule> _schedules;

    public PersonCalendar(IEnumerable<Policy> policies, IEnumerable<Schedule> schedules)
    {
        _policies = policies.OrderBy(p => p.EffectiveFrom).ToList();
        _schedules = schedules.OrderBy(s => s.EffectiveFrom).ToList();
    }

    public Policy PolicyOn(DateOnly day) => _policies.LastOrDefault(p => p.EffectiveFrom <= day) ?? _policies[0];

    /// <summary>The paid minutes, break included, of a week on the schedule (or the company week) in force on <paramref name="day"/>.</summary>
    public int PaidMinutesPerWeek(DateOnly day)
    {
        var policy = PolicyOn(day);
        var schedule = _schedules.LastOrDefault(s => s.EffectiveFrom <= day);

        return schedule is { FollowsDefault: false }
            ? schedule.Days.Values.Sum(d => d.End - d.Start)
            : Enumerable.Range(0, 7).Count(weekday => (policy.WorkDays & (1 << weekday)) != 0) * (policy.End - policy.Start);
    }

    /// <returns>The hours worked that day, or null on a day off.</returns>
    public WorkHours? HoursOn(DateOnly day)
    {
        var policy = PolicyOn(day);
        var weekday = (int)day.DayOfWeek;
        var schedule = _schedules.LastOrDefault(s => s.EffectiveFrom <= day);

        if (schedule is { FollowsDefault: false })
        {
            return schedule.Days.TryGetValue(weekday, out var own) ? new WorkHours(own.Start, own.End, policy.Break) : null;
        }

        return (policy.WorkDays & (1 << weekday)) != 0 ? new WorkHours(policy.Start, policy.End, policy.Break) : null;
    }
}
