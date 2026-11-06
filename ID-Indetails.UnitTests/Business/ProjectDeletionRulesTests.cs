using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectDeletionRulesTests
{
    private static readonly ProjectDeletionRules.Blocking Nothing = new(0, 0, 0, 0, 0, 0);

    [Fact]
    public void An_archived_project_that_holds_nothing_real_can_be_deleted()
        => ProjectDeletionRules.Blockers(ProjectStatus.Archived, Nothing).ShouldBeEmpty();

    [Fact]
    public void Only_an_archived_project_can()
    {
        foreach (var status in new[] { ProjectStatus.Active, ProjectStatus.Hold, ProjectStatus.Completed })
        {
            ProjectDeletionRules.Blockers(status, Nothing).ShouldBe(["It isn't archived. Archive it first."]);
        }
    }

    [Fact]
    public void Money_that_moved_keeps_it_and_each_is_said()
    {
        var blockers = ProjectDeletionRules.Blockers(ProjectStatus.Archived, Nothing with { IssuedInvoices = 2, Payments = 1, PocketMoves = 3, SupervisionTaken = 1 });

        blockers.ShouldBe([
            "2 issued invoices, voided ones included: their numbers must stay.",
            "1 payment received from the client.",
            "3 pocket money moves.",
            "1 supervision withdrawal.",
        ]);
    }

    [Fact]
    public void Hours_keep_it_for_good_once_a_month_has_closed_and_until_moved_otherwise()
    {
        ProjectDeletionRules.Blockers(ProjectStatus.Archived, Nothing with { Hours = 5, HoursInClosedMonths = 2 })
            .ShouldBe(["5 hours entries logged on it, 2 in months already closed, which never change."]);
        ProjectDeletionRules.Blockers(ProjectStatus.Archived, Nothing with { Hours = 1 })
            .ShouldBe(["1 hours entry logged this month: reopen the project and move them to the right one first."]);
    }

    [Fact]
    public void Its_name_typed_exactly_confirms_it()
    {
        ProjectDeletionRules.CheckConfirmed("Villa Kadmous", "Villa Kadmous").ShouldBeNull();
        ProjectDeletionRules.CheckConfirmed("Villa Kadmous", "  Villa Kadmous ").ShouldBeNull();
        ProjectDeletionRules.CheckConfirmed("Villa Kadmous", "villa kadmous").ShouldNotBeNull().Field.ShouldBe("title");
        ProjectDeletionRules.CheckConfirmed("Villa Kadmous", null).ShouldNotBeNull();
    }

    [Fact]
    public void Counts_are_worded_one_or_many()
    {
        ProjectDeletionRules.Count(1, "site expense").ShouldBe("1 site expense");
        ProjectDeletionRules.Count(1_200, "site expense").ShouldBe("1,200 site expenses");
        ProjectDeletionRules.Count(2, "BOQ or VO", "BOQs and VOs").ShouldBe("2 BOQs and VOs");
    }
}
