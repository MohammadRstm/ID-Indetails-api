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
    public void New_meetings_are_set_on_active_projects_only()
    {
        MeetingRules.CheckNewMeeting(ProjectStatus.Active).ShouldBeNull();
        MeetingRules.CheckNewMeeting(ProjectStatus.Hold)!.Code.ShouldBe(ErrorCodes.ProjectNotActive);
        MeetingRules.CheckNewMeeting(ProjectStatus.Completed)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
        MeetingRules.CheckNewMeeting(ProjectStatus.Archived)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
    }

    [Fact]
    public void Meetings_already_set_change_while_their_project_is_open()
    {
        MeetingRules.CheckProject(ProjectStatus.Active).ShouldBeNull();
        MeetingRules.CheckProject(ProjectStatus.Hold).ShouldBeNull();
        MeetingRules.CheckProject(ProjectStatus.Completed)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
        MeetingRules.CheckProject(ProjectStatus.Archived)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
    }

    [Fact]
    public void A_general_meeting_is_seen_by_its_people_and_whoever_manages_meetings_where_it_belongs()
    {
        var designMeeting = new MeetingRules.GeneralMeeting(DomainCodes.Design, CreatedById: 1, AttendeeIds: [2]);
        var companyMeeting = designMeeting with { Domain = null };
        MeetingRules.Viewer Person(long id, bool manages = false, bool company = false, params string[] domains)
            => new(id, manages, company, domains.Length > 0 ? domains : [DomainCodes.Design]);

        MeetingRules.CanSeeGeneral(designMeeting, Person(1)).ShouldBeTrue();
        MeetingRules.CanSeeGeneral(designMeeting, Person(2)).ShouldBeTrue();
        MeetingRules.CanSeeGeneral(designMeeting, Person(3)).ShouldBeFalse();
        MeetingRules.CanSeeGeneral(designMeeting, Person(4, manages: true)).ShouldBeTrue();
        MeetingRules.CanSeeGeneral(designMeeting, Person(5, manages: true, domains: DomainCodes.Execution)).ShouldBeFalse();

        // company-wide: its people and whoever sees the whole company
        MeetingRules.CanSeeGeneral(companyMeeting, Person(2)).ShouldBeTrue();
        MeetingRules.CanSeeGeneral(companyMeeting, Person(4, manages: true)).ShouldBeFalse();
        MeetingRules.CanSeeGeneral(companyMeeting, Person(6, manages: true, company: true)).ShouldBeTrue();
    }

    [Fact]
    public void A_general_meeting_is_with_at_least_one_person_its_setter_can_see()
    {
        var invitable = new HashSet<long> { 2, 3 };

        MeetingRules.CheckAttendees([2], invitable).ShouldBeNull();
        MeetingRules.CheckAttendees([], invitable)!.Field.ShouldBe("attendeeIds");
        MeetingRules.CheckAttendees([2, 9], invitable)!.Field.ShouldBe("attendeeIds");
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
