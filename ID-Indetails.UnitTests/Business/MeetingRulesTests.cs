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
    public void Meetings_are_set_on_open_design_projects()
    {
        MeetingRules.CheckProject(DomainCodes.Design, ProjectStatus.Active).ShouldBeNull();
        MeetingRules.CheckProject(DomainCodes.Design, ProjectStatus.Hold).ShouldBeNull();
        MeetingRules.CheckProject(DomainCodes.Design, ProjectStatus.Completed)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
        MeetingRules.CheckProject(DomainCodes.Execution, ProjectStatus.Active)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public void A_meeting_lasts_at_least_5_minutes_within_one_day()
    {
        MeetingRules.CheckTimes(Ten, Ten.AddMinutes(5), Day, Day).ShouldBeNull();
        MeetingRules.CheckTimes(Ten, Ten.AddMinutes(4), Day, Day)!.Field.ShouldBe("endsAt");
        MeetingRules.CheckTimes(Ten, Ten.AddHours(20), Day, Day.AddDays(1))!.Field.ShouldBe("endsAt");
    }
}
