using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Services.Push.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ReminderRulesTests
{
    /// <summary>09:00 to 17:00 with a 60-minute paid break: 7 hours required.</summary>
    private static readonly WorkHours NineToFive = new(9 * 60, 17 * 60, 60);

    [Theory]
    [InlineData(15 * 60 + 59, false)]
    [InlineData(16 * 60, true)]
    [InlineData(16 * 60 + 59, true)]
    [InlineData(17 * 60, false)]
    public void The_log_reminder_comes_in_the_last_hour_of_the_working_day(int minute, bool due)
        => ReminderRules.LogReminderDue(NineToFive, minute, loggedInsideMinutes: 120, excusedToday: false).ShouldBe(due);

    [Fact]
    public void Nobody_is_reminded_who_logged_the_day_on_a_day_off_or_when_excused()
    {
        ReminderRules.LogReminderDue(NineToFive, 16 * 60 + 30, NineToFive.Required, false).ShouldBeFalse();
        ReminderRules.LogReminderDue(NineToFive, 16 * 60 + 30, NineToFive.Required - 1, false).ShouldBeTrue();
        ReminderRules.LogReminderDue(null, 16 * 60 + 30, 0, false).ShouldBeFalse();
        ReminderRules.LogReminderDue(NineToFive, 16 * 60 + 30, 0, excusedToday: true).ShouldBeFalse();
    }

    [Fact]
    public void The_labour_reminder_comes_on_Saturday_from_15_00()
    {
        var saturday = new DateOnly(2026, 10, 10);

        ReminderRules.LabourReminderDue(saturday, 14 * 60 + 59).ShouldBeFalse();
        ReminderRules.LabourReminderDue(saturday, 15 * 60).ShouldBeTrue();
        ReminderRules.LabourReminderDue(saturday.AddDays(-1), 16 * 60).ShouldBeFalse();
        ReminderRules.LabourReminderDue(saturday.AddDays(1), 16 * 60).ShouldBeFalse();
    }

    [Fact]
    public void A_reminder_goes_once_a_day_per_person_and_site()
    {
        var log = new ReminderLog();
        var day = new DateOnly(2026, 10, 10);

        log.TryMark(1, PushKinds.LabourWeekReminder, 7, day).ShouldBeTrue();
        log.TryMark(1, PushKinds.LabourWeekReminder, 7, day).ShouldBeFalse();
        log.TryMark(1, PushKinds.LabourWeekReminder, 8, day).ShouldBeTrue();
        log.TryMark(2, PushKinds.LabourWeekReminder, 7, day).ShouldBeTrue();
        log.WasSent(1, PushKinds.LabourWeekReminder, 7, day).ShouldBeTrue();
        log.TryMark(1, PushKinds.LabourWeekReminder, 7, day.AddDays(7)).ShouldBeTrue();
    }
}
