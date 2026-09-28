using ID_Indetails.api.Business.Labour;

namespace ID_Indetails.api.Business.Execution;

/// <summary>
/// What one person recorded on Execution sites: the materials they added and the labour weeks they
/// submitted. It is the site-staff counterpart of logged hours, since nobody on site logs time.
/// Materials are dated by when they were added, labour weeks by when they were submitted.
/// </summary>
public static class SiteRecords
{
    public const int WeeksShown = 16;

    public enum Kind
    {
        Material,
        LabourWeek,
    }

    /// <param name="Day">The company-time-zone day it was recorded.</param>
    /// <param name="Label">The material's type, or the Monday of the labour week.</param>
    public sealed record Record(Kind Kind, long Id, long ProjectId, DateOnly Day, DateTime At, string Label, decimal Amount);

    public sealed record Week(DateOnly WeekStart, decimal Materials, decimal Labour)
    {
        public decimal Value => Materials + Labour;
    }

    public sealed record ProjectTotals(long ProjectId, int MaterialCount, decimal Materials, int LabourWeeks, decimal Labour, DateTime LastRecordedAt);

    public sealed record Summary(
        int MaterialCount,
        decimal Materials,
        int LabourWeeks,
        decimal Labour,
        decimal ThisQuarter,
        int ActiveDays,
        IReadOnlyList<Week> Weeks,
        IReadOnlyList<ProjectTotals> Projects)
    {
        public decimal Total => Materials + Labour;
    }

    public static Summary Summarise(IReadOnlyList<Record> records, DateOnly today)
    {
        var quarterStart = new DateOnly(today.Year, (today.Month - 1) / 3 * 3 + 1, 1);
        var thisWeek = LabourRules.WeekStartOf(today);

        var weeks = Enumerable.Range(0, WeeksShown)
            .Select(i => thisWeek.AddDays(-7 * (WeeksShown - 1 - i)))
            .Select(start =>
            {
                var inWeek = records.Where(r => r.Day >= start && r.Day < start.AddDays(7)).ToList();
                return new Week(start, Sum(inWeek, Kind.Material), Sum(inWeek, Kind.LabourWeek));
            })
            .ToList();

        var projects = records
            .GroupBy(r => r.ProjectId)
            .Select(g => new ProjectTotals(
                g.Key,
                g.Count(r => r.Kind == Kind.Material),
                Sum(g, Kind.Material),
                g.Count(r => r.Kind == Kind.LabourWeek),
                Sum(g, Kind.LabourWeek),
                g.Max(r => r.At)))
            .OrderByDescending(p => p.LastRecordedAt)
            .ToList();

        return new Summary(
            records.Count(r => r.Kind == Kind.Material),
            Sum(records, Kind.Material),
            records.Count(r => r.Kind == Kind.LabourWeek),
            Sum(records, Kind.LabourWeek),
            records.Where(r => r.Day >= quarterStart && r.Day <= today).Sum(r => r.Amount),
            records.Select(r => r.Day).Distinct().Count(),
            weeks,
            projects);
    }

    private static decimal Sum(IEnumerable<Record> records, Kind kind) => records.Where(r => r.Kind == kind).Sum(r => r.Amount);
}
