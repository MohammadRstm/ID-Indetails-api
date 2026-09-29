using ID_Indetails.api.Business.Requirements;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MyRequirementsTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);
    private static readonly DateTime Created = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static MyRequirements.Item Item(long id, DateOnly? deadline, bool done = false, long project = 1, int createdDaysLater = 0)
        => new(id, project, deadline, done, Created.AddDays(createdDaysLater));

    [Fact]
    public void Open_requirements_come_soonest_deadline_first_and_undated_last_oldest_first()
    {
        var summary = MyRequirements.Summarise(
        [
            Item(1, null, createdDaysLater: 2),
            Item(2, Today.AddDays(10)),
            Item(3, Today.AddDays(-3)),
            Item(4, null),
            Item(5, Today),
            Item(6, Today.AddDays(1), done: true),
        ], Today);

        summary.Open.Select(i => i.Id).ShouldBe([3, 5, 2, 4, 1]);
    }

    [Fact]
    public void Counts_cover_overdue_due_within_a_week_projects_and_done()
    {
        var summary = MyRequirements.Summarise(
        [
            Item(1, Today.AddDays(-1), project: 1),
            Item(2, Today, project: 2),
            Item(3, Today.AddDays(MyRequirements.SoonDays), project: 2),
            Item(4, Today.AddDays(MyRequirements.SoonDays + 1), project: 3),
            Item(5, Today.AddDays(-5), done: true, project: 4),
            Item(6, null, done: true, project: 4),
        ], Today);

        summary.ShouldSatisfyAllConditions(
            s => s.Overdue.ShouldBe(1),
            s => s.DueSoon.ShouldBe(2),
            s => s.Projects.ShouldBe(3),
            s => s.Done.ShouldBe(2));
    }
}
