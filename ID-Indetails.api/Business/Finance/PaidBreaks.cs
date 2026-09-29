using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// The daily break is paid: 7 required hours are 8 paid hours. Project cost counts everyone's paid break as labour, hourly
/// and salaried alike, on the days Salaries pays it: each working day they did some work within their hours, the day's
/// paid break (none on days of 6 hours or less), at that day's rate, shared across the day's projects by the in-hours
/// minutes worked on each. (Project pay leaves breaks out; that is a separate report.)
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
            shares.AddRange(day
                .GroupBy(s => projectOf[s.Id])
                .Select(g => new Share(g.Key, day.Key, breakMinutes * (decimal)g.Sum(s => s.Normal) / normal)));
        }

        return shares;
    }

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
