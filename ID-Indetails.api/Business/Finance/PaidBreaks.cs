using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// The daily break is paid: 7 required hours are 8 paid hours. Project cost counts everyone's paid break as labour, at
/// that day's rate, shared across the day's projects by the in-hours minutes worked on each (decided 19 November 2026):
/// <list type="bullet">
/// <item>a day with its required hours logged shares the whole break (none on days of 6 hours or less), less any of it
/// worked: from <see cref="WorkWeek.BreakCountsFrom"/> a full 9 to 5 logged took no break, so none is shared;</item>
/// <item>a day logged only in part shares that part of it: 5 of 7 required hours, 5/7 of the break. The rest is still paid,
/// a company cost (<see cref="Unshared"/>);</item>
/// <item>in whole minutes, the remainder to the project with the most of the day.</item>
/// </list>
/// Overtime (before or after the hours, or past the required ones) takes no share: the break was taken during the day.
/// (Project pay leaves breaks out; that is a separate report.)
/// </summary>
public static class PaidBreaks
{
    /// <param name="Start">Minutes after midnight (company time) the work started.</param>
    public sealed record Work(long Id, long ProjectId, DateOnly Day, int Start, int Minutes);

    public sealed record Share(long ProjectId, DateOnly Day, decimal Minutes);

    /// <summary>One person's paid break minutes charged to each project, day by day. Needs all their work of those days.</summary>
    public static IReadOnlyList<Share> Allocate(IReadOnlyCollection<Work> work, PersonCalendar calendar)
    {
        var projectOf = work.ToDictionary(w => w.Id, w => w.ProjectId);
        var shares = new List<Share>();

        foreach (var day in Overtime.Of(work.Select(w => new Overtime.Work(w.Id, w.Day, w.Start, w.Minutes)), calendar)
                     .Where(s => s.Normal > 0)
                     .GroupBy(s => s.Day))
        {
            var breakMinutes = calendar.HoursOn(day.Key)?.PaidBreak ?? 0;
            if (breakMinutes <= 0)
            {
                continue;
            }

            var normal = day.Sum(s => s.Normal);
            var shared = calendar.HoursOn(day.Key)!.BreakTaken(normal);
            if (shared <= 0)
            {
                continue;
            }

            var projects = day
                .GroupBy(s => projectOf[s.Id])
                .Select(g => (ProjectId: g.Key, Normal: g.Sum(s => s.Normal)))
                .OrderByDescending(p => p.Normal)
                .ThenBy(p => p.ProjectId)
                .ToList();
            var whole = projects.Select(p => (int)Math.Floor(shared * (decimal)p.Normal / normal)).ToList();
            whole[0] += shared - whole.Sum();
            shares.AddRange(projects.Select((p, i) => new Share(p.ProjectId, day.Key, whole[i])).Where(s => s.Minutes > 0));
        }

        return shares;
    }

    /// <summary>
    /// The paid break minutes no project carries, day by day: the rest of the break of a day logged only in part. Salaries
    /// pays the whole break, so the company overview counts these as a company cost.
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, int Minutes)> Unshared(IReadOnlyCollection<Work> work, PersonCalendar calendar)
        => Overtime.Of(work.Select(w => new Overtime.Work(w.Id, w.Day, w.Start, w.Minutes)), calendar)
            .Where(s => s.Normal > 0)
            .GroupBy(s => s.Day)
            .Select(day => (Day: day.Key, Hours: calendar.HoursOn(day.Key), Normal: day.Sum(s => s.Normal)))
            .Where(d => d.Hours is { PaidBreak: > 0 })
            .Select(d => (d.Day, d.Hours!.BreakUnshared(d.Normal)))
            .Where(d => d.Item2 > 0)
            .ToList();

    /// <summary>The shares as labour cost at the person's rate that day.</summary>
    public static IReadOnlyList<(long ProjectId, ProjectFinance.CostItem Cost)> Price(
        IEnumerable<Share> shares,
        IReadOnlyList<ProjectFinance.PayRate> history)
    {
        var ordered = history.OrderBy(r => r.EffectiveFrom).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        return shares
            .Select(s => (Share: s, Rate: ProjectFinance.RateOn(ordered, s.Day)))
            .Select(x => (x.Share.ProjectId, new ProjectFinance.CostItem(
                CostSource.Labour, x.Rate.Currency, x.Share.Minutes / 60m * x.Rate.HourlyCost, x.Share.Day)))
            .ToList();
    }
}
