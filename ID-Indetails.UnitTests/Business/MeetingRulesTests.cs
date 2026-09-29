using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MeetingRulesTests
{
    private static readonly DateTime Ten = new(2026, 9, 28, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 9, 28);

    [Fact]
    public void Meetings_are_set_on_open_projects()
    {
        MeetingRules.CheckProject(ProjectStatus.Active).ShouldBeNull();
        MeetingRules.CheckProject(ProjectStatus.Hold).ShouldBeNull();
        MeetingRules.CheckProject(ProjectStatus.Completed)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
        MeetingRules.CheckProject(ProjectStatus.Archived)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
    }

    [Fact]
    public void A_meeting_lasts_at_least_5_minutes_within_one_day()
    {
        MeetingRules.CheckTimes(Ten, Ten.AddMinutes(5), Day, Day).ShouldBeNull();
        MeetingRules.CheckTimes(Ten, Ten.AddMinutes(4), Day, Day)!.Field.ShouldBe("endsAt");
        MeetingRules.CheckTimes(Ten, Ten.AddHours(20), Day, Day.AddDays(1))!.Field.ShouldBe("endsAt");
    }

    [Fact]
    public void Meeting_setters_change_their_own_and_managers_change_any()
    {
        MeetingRules.CheckChange(managesMeetings: true, setsMeetings: false, createdById: 9, actorId: 1).ShouldBeNull();
        MeetingRules.CheckChange(managesMeetings: false, setsMeetings: true, createdById: 1, actorId: 1).ShouldBeNull();
        MeetingRules.CheckChange(managesMeetings: false, setsMeetings: true, createdById: 9, actorId: 1)!.Code.ShouldBe(ErrorCodes.Forbidden);
        MeetingRules.CheckChange(managesMeetings: false, setsMeetings: true, createdById: null, actorId: 1).ShouldNotBeNull();
    }
}
