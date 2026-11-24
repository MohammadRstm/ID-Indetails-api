namespace ID_Indetails.api.Business.WorkingTime;

/// <summary>A day's working hours; <see cref="Required"/> is the span minus the paid break.</summary>
/// <param name="BreakCounts">
/// From <see cref="WorkWeek.BreakCountsFrom"/> the break counts in a person's hours: a day expects its whole span, the
/// break taken is added to what they worked, and time inside the hours past the required ones is the break worked,
/// not overtime. Before, a day expected its required hours and the time past them inside the hours was overtime.
/// </param>
public sealed record WorkHours(int Start, int End, int Break, bool BreakCounts = false)
{
    public int Required => WorkWeek.RequiredMinutes(Start, End, Break);

    /// <summary>The part of the span not required: the paid break (0 on days of 6 hours or less).</summary>
    public int PaidBreak => End - Start - Required;

    public int Span => End - Start;

    /// <summary>The day's hours in a person's month: the whole span once the break counts, its required hours before.</summary>
    public int Expected => BreakCounts ? Span : Required;

    /// <summary>The minutes inside the hours that are normal work; any more inside them is overtime.</summary>
    public int InHours => BreakCounts ? Span : Required;

    /// <summary>
    /// The minutes of the break taken on a day with this much normal work, which its projects carry: the whole break
    /// once the required hours are in, less any of it worked (a full 9 to 5 logged takes none); on a day short of
    /// them, that part of it (5 of 7 required hours, 5/7 of the break).
    /// </summary>
    public int BreakTaken(int normal)
        => normal <= 0 || PaidBreak <= 0 ? 0
            : normal >= Required ? Math.Max(0, PaidBreak - (normal - Required))
            : (int)Math.Round(PaidBreak * (decimal)normal / Required, MidpointRounding.AwayFromZero);

    /// <summary>The paid break no project carries: the rest of the break of a day short of its required hours, a company cost.</summary>
    public int BreakUnshared(int normal)
        => normal <= 0 ? 0 : Math.Max(0, PaidBreak - BreakTaken(normal) - Math.Max(0, normal - Required));
}

/// <summary>
/// One person's working days over time: their own schedule where they have one, otherwise the
/// company week, each taken from the version in force on the day. The break and the overtime
/// multiplier always come from the company policy.
/// </summary>
public sealed class PersonCalendar
{
    /// <param name="OvertimeThresholdHours">A salary's overtime is paid only past this many hours a month; 0 pays all of it.</param>
    public sealed record Policy(
        DateOnly EffectiveFrom, byte WorkDays, int Start, int End, int Break, decimal OvertimeMultiplier, int OvertimeThresholdHours = 0);

    public sealed record Schedule(DateOnly EffectiveFrom, bool FollowsDefault, IReadOnlyDictionary<int, (int Start, int End)> Days);

    private readonly List<Policy> _policies;
    private readonly List<Schedule> _schedules;

    public PersonCalendar(IEnumerable<Policy> policies, IEnumerable<Schedule> schedules)
    {
        _policies = policies.OrderBy(p => p.EffectiveFrom).ToList();
        _schedules = schedules.OrderBy(s => s.EffectiveFrom).ToList();
    }

    public Policy PolicyOn(DateOnly day) => _policies.LastOrDefault(p => p.EffectiveFrom <= day) ?? _policies[0];

    /// <returns>The hours worked that day, or null on a day off.</returns>
    public WorkHours? HoursOn(DateOnly day)
    {
        var policy = PolicyOn(day);
        var weekday = (int)day.DayOfWeek;
        var schedule = _schedules.LastOrDefault(s => s.EffectiveFrom <= day);

        var counts = day >= WorkWeek.BreakCountsFrom;

        if (schedule is { FollowsDefault: false })
        {
            return schedule.Days.TryGetValue(weekday, out var own) ? new WorkHours(own.Start, own.End, policy.Break, counts) : null;
        }

        return (policy.WorkDays & (1 << weekday)) != 0 ? new WorkHours(policy.Start, policy.End, policy.Break, counts) : null;
    }
}
