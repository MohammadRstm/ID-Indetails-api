using ID_Indetails.api.Business.Execution;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class SiteRecordsTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    private static SiteRecords.Record Material(long project, DateOnly day, decimal amount)
        => new(SiteRecords.Kind.Material, 1, project, day, day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), "Cement", amount);

    private static SiteRecords.Record Week(long project, DateOnly day, decimal amount)
        => new(SiteRecords.Kind.LabourWeek, 2, project, day, day.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc), "2026-09-14", amount);

    [Fact]
    public void Records_add_up_by_kind_quarter_and_active_day()
    {
        var summary = SiteRecords.Summarise(
            [Material(1, Today, 100), Material(1, Today, 50), Week(2, new DateOnly(2026, 9, 21), 400), Material(2, new DateOnly(2026, 6, 30), 70)],
            Today);

        summary.ShouldSatisfyAllConditions(
            s => s.MaterialCount.ShouldBe(3),
            s => s.Materials.ShouldBe(220m),
            s => s.LabourWeeks.ShouldBe(1),
            s => s.Labour.ShouldBe(400m),
            s => s.Total.ShouldBe(620m),
            s => s.ThisQuarter.ShouldBe(550m),
            s => s.ActiveDays.ShouldBe(3));
    }

    [Fact]
    public void The_last_sixteen_weeks_end_with_the_current_one()
    {
        var summary = SiteRecords.Summarise([Material(1, Today, 100), Week(1, new DateOnly(2026, 9, 21), 400), Material(1, new DateOnly(2026, 9, 20), 30)], Today);

        summary.Weeks.Count.ShouldBe(SiteRecords.WeeksShown);
        summary.Weeks[^1].ShouldBe(new SiteRecords.Week(new DateOnly(2026, 9, 21), 100, 400));
        summary.Weeks[^2].ShouldBe(new SiteRecords.Week(new DateOnly(2026, 9, 14), 30, 0));
        summary.Weeks[0].WeekStart.ShouldBe(new DateOnly(2026, 6, 8));
    }

    [Fact]
    public void Projects_are_listed_most_recently_recorded_first()
    {
        var summary = SiteRecords.Summarise([Material(1, new DateOnly(2026, 9, 1), 10), Week(2, new DateOnly(2026, 9, 10), 400), Material(2, new DateOnly(2026, 9, 2), 5)], Today);

        summary.Projects.Select(p => (p.ProjectId, p.MaterialCount, p.Materials, p.LabourWeeks, p.Labour))
            .ShouldBe([(2L, 1, 5m, 1, 400m), (1L, 1, 10m, 0, 0m)]);
    }
}
