using System.Text.Json;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PushRulesTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Every_kind_has_the_agreed_category_and_channel()
    {
        foreach (var kind in PushTextTests.AllKinds)
        {
            PushCategories.Of(kind).ShouldNotBeNull(kind);
        }

        PushCategories.Of(NotificationKinds.RequirementOverdue).ShouldBe(NotificationCategory.Requirements);
        PushCategories.Of(NotificationKinds.HoursExcused).ShouldBe(NotificationCategory.Work);
        PushCategories.Of(NotificationKinds.HoursShort).ShouldBe(NotificationCategory.Money);
        PushCategories.Of(NotificationKinds.ClientAdded).ShouldBe(NotificationCategory.Money);
        PushCategories.Of(NotificationKinds.TeamChanged).ShouldBe(NotificationCategory.People);
        PushCategories.Of(PushKinds.LabourWeekReminder).ShouldBe(NotificationCategory.Sites);
        PushCategories.Of(PushKinds.Message).ShouldBe(NotificationCategory.Messages);
        PushCategories.Of(PushKinds.LogReminder).ShouldBeNull();

        PushCategories.ChannelOf(NotificationKinds.MaterialsAdded).ShouldBe("sites");
        PushCategories.ChannelOf(NotificationKinds.PaymentRecorded).ShouldBe("money");
        PushCategories.ChannelOf(PushKinds.Message).ShouldBe("messages");
        PushCategories.ChannelOf(PushKinds.LogReminder).ShouldBe("reminders");
    }

    [Theory]
    [InlineData("22:00", true)]
    [InlineData("23:59", true)]
    [InlineData("00:00", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("12:00", false)]
    [InlineData("21:59", false)]
    public void Default_quiet_hours_run_from_22_00_to_07_00_across_midnight(string time, bool quiet)
        => QuietHours.Contains(QuietHours.DefaultFromMinute, QuietHours.DefaultToMinute, TimeOnly.Parse(time)).ShouldBe(quiet);

    [Fact]
    public void Quiet_hours_within_a_day_and_none_at_all()
    {
        QuietHours.Contains(13 * 60, 14 * 60, new TimeOnly(13, 30)).ShouldBeTrue();
        QuietHours.Contains(13 * 60, 14 * 60, new TimeOnly(14, 0)).ShouldBeFalse();
        QuietHours.Contains(13 * 60, 14 * 60, new TimeOnly(12, 59)).ShouldBeFalse();
        QuietHours.Contains(null, null, new TimeOnly(23, 0)).ShouldBeFalse();
        QuietHours.Contains(600, 600, new TimeOnly(10, 0)).ShouldBeFalse();
    }

    [Fact]
    public void A_digest_pushes_in_full_at_most_once_an_hour()
    {
        var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        DigestPushes.PushesInFull(null, now).ShouldBeTrue();
        DigestPushes.PushesInFull(now.AddMinutes(-59), now).ShouldBeFalse();
        DigestPushes.PushesInFull(now.AddHours(-1), now).ShouldBeTrue();
    }

    [Fact]
    public void Requirements_open_the_requirement_and_project_news_the_project()
    {
        PushTarget.ForNotification(NotificationKinds.RequirementAssigned, 7, DomainCodes.Design, "requirement", 70, null)
            .ShouldBe(new PushTarget { Type = PushTarget.Requirement, ProjectId = 7, RequirementId = 70, Domain = DomainCodes.Design });
        PushTarget.ForNotification(NotificationKinds.RequirementsAdded, 7, DomainCodes.Design, "requirement", 71, null)
            .ShouldBe(new PushTarget { Type = PushTarget.Project, Id = 7, Domain = DomainCodes.Design, Section = "requirements" });
        PushTarget.ForNotification(NotificationKinds.MeetingScheduled, 7, DomainCodes.Execution, "meeting", 3, null).Section.ShouldBe("meetings");
        PushTarget.ForNotification(NotificationKinds.MaterialsAdded, 9, DomainCodes.Execution, "project", 9, null).Section.ShouldBe("materials");
        PushTarget.ForNotification(NotificationKinds.ProjectStatusChanged, 7, DomainCodes.Design, "project", 7, null)
            .ShouldBe(new PushTarget { Type = PushTarget.Project, Id = 7, Domain = DomainCodes.Design });
        PushTarget.ForNotification(NotificationKinds.ProjectCostHigh, 7, DomainCodes.Design, "project", 7, null).Type.ShouldBe(PushTarget.Project);
    }

    [Fact]
    public void A_labour_week_opens_that_week()
    {
        PushTarget.ForNotification(NotificationKinds.LabourWeekSubmitted, 9, DomainCodes.Execution, "project", 9, Payload("""{"weekStart":"2026-10-05"}"""))
            .ShouldBe(PushTarget.LabourWeek(9, new DateOnly(2026, 10, 5)));
        PushTarget.ForNotification(NotificationKinds.LabourWeekSubmitted, 9, DomainCodes.Execution, "project", 9, null).Section.ShouldBe("labour");
    }

    [Fact]
    public void Records_open_their_own_screens()
    {
        PushTarget.ForNotification(NotificationKinds.WorkLogged, 7, DomainCodes.Design, "work_entry", 12, null)
            .ShouldBe(new PushTarget { Type = PushTarget.WorkEntry, Id = 12 });
        PushTarget.ForNotification(NotificationKinds.EntryComment, 7, DomainCodes.Design, "work_entry", 12, null).Type.ShouldBe(PushTarget.WorkEntry);
        PushTarget.ForNotification(NotificationKinds.InvitationAccepted, null, null, "user", 5, null).ShouldBe(new PushTarget { Type = PushTarget.Person, Id = 5 });
        PushTarget.ForNotification(NotificationKinds.ClientAdded, null, null, "client", 3, null).ShouldBe(new PushTarget { Type = PushTarget.Client, Id = 3 });
        PushTarget.ForNotification(NotificationKinds.InvoiceOverdue, 7, DomainCodes.Design, "invoice", 8, null).ShouldBe(new PushTarget { Type = PushTarget.Invoice, Id = 8 });
        PushTarget.ForNotification(NotificationKinds.PaymentRecorded, 7, DomainCodes.Design, "payment", 4, null).ShouldBe(new PushTarget { Type = PushTarget.Payment, Id = 4 });
        PushTarget.ForNotification(NotificationKinds.StatementSent, 9, DomainCodes.Execution, "statement", 2, null).ShouldBe(new PushTarget { Type = PushTarget.Statement, Id = 2 });
        PushTarget.ForNotification(NotificationKinds.WorkDigest, 7, DomainCodes.Design, null, null, null).ShouldBe(PushTarget.OfPage("work"));
    }

    [Fact]
    public void Anything_without_its_own_screen_or_missing_what_it_needs_opens_the_notifications()
    {
        PushTarget.ForNotification(NotificationKinds.RoleChanged, null, null, "user", 5, null).ShouldBe(PushTarget.Notifications);
        PushTarget.ForNotification(NotificationKinds.PayChanged, null, null, "user", 5, null).ShouldBe(PushTarget.Notifications);
        PushTarget.ForNotification(NotificationKinds.ProjectCreated, null, null, "project", null, null).ShouldBe(PushTarget.Notifications);
        PushTarget.ForNotification(NotificationKinds.RequirementDone, 7, DomainCodes.Design, "project", 7, null).ShouldBe(PushTarget.Notifications);
        PushTarget.ForNotification("something_new", 7, DomainCodes.Design, null, null, null).ShouldBe(PushTarget.Notifications);
    }

    [Fact]
    public void A_message_opens_its_chat()
    {
        PushTarget.Chat(4, 20).ShouldBe(new PushTarget { Type = PushTarget.Conversation, TeamId = 4 });
        PushTarget.Chat(null, 20).ShouldBe(new PushTarget { Type = PushTarget.Conversation, UserId = 20 });
    }

    [Fact]
    public void Preferences_mute_whole_categories_but_never_the_log_reminder()
    {
        var preferences = new PushPreferences(null, null, new HashSet<NotificationCategory> { NotificationCategory.Money, NotificationCategory.Messages });

        preferences.Mutes(NotificationKinds.PaymentRecorded).ShouldBeTrue();
        preferences.Mutes(PushKinds.Message).ShouldBeTrue();
        preferences.Mutes(NotificationKinds.RequirementAssigned).ShouldBeFalse();
        preferences.Mutes(PushKinds.LogReminder).ShouldBeFalse();
        preferences.QuietAt(new TimeOnly(23, 0)).ShouldBeFalse();
        PushPreferences.Default.QuietAt(new TimeOnly(23, 0)).ShouldBeTrue();
        PushPreferences.Default.Muted.ShouldBeEmpty();
    }
}
