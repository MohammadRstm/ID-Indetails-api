namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// Someone's periods of employment, oldest first. A person who left and was taken back has a gap between two
/// periods: pay and the hours ledger skip its days, so they aren't paid, or counted short, for time they were away.
/// </summary>
public sealed record Employment(IReadOnlyList<Employment.Period> Periods)
{
    /// <param name="EndedOn">The last working day; null while it lasts.</param>
    public sealed record Period(DateOnly StartedOn, DateOnly? EndedOn);

    /// <summary>Days between two periods, both ends included.</summary>
    public sealed record Gap(DateOnly From, DateOnly To);

    public DateOnly JoinedOn => Periods[0].StartedOn;

    /// <summary>The last working day of the last period; null while they are employed.</summary>
    public DateOnly? LeftOn => Periods[^1].EndedOn;

    public IReadOnlyList<Gap> Gaps => Periods
        .Zip(Periods.Skip(1), (before, after) => new Gap(before.EndedOn!.Value.AddDays(1), after.StartedOn.AddDays(-1)))
        .Where(g => g.From <= g.To)
        .ToList();

    public static bool InGap(IReadOnlyList<Gap>? gaps, DateOnly day) => gaps is not null && gaps.Any(g => g.From <= day && day <= g.To);
}
