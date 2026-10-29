using System.Reflection;
using System.Text.Json;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PushTextTests
{
    /// <summary>Every kind the API writes to <c>notifications.kind</c>.</summary>
    public static readonly IReadOnlyList<string> AllKinds = typeof(NotificationKinds)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    public static TheoryData<string> Kinds() => new(AllKinds);

    public static TheoryData<string> MoneyKinds() => new(AllKinds.Where(k => PushCategories.Of(k) == NotificationCategory.Money));

    /// <summary>A payload holding everything a lock screen must never show.</summary>
    private static readonly JsonElement Revealing = JsonDocument.Parse("""
        {
          "name": "Haddad Holdings", "organisation": "Haddad Holdings SAL", "number": "INV-2026-031", "invoiceNumber": "INV-2026-031",
          "amount": 48250.75, "marginPct": 17.4, "usedPct": 91, "to": "accounts@haddad.example", "sendTo": "accounts@haddad.example",
          "projectTitle": "Villa Haddad", "isInitial": true, "dueOn": "2026-10-01", "effectiveFrom": "2026-10-01", "month": "2026-09",
          "count": 3, "people": [{ "id": 4, "name": "Sami Khoury", "shortMinutes": 615 }]
        }
        """).RootElement;

    [Fact]
    public void There_are_52_notification_kinds_and_each_has_its_own_text()
    {
        AllKinds.Count.ShouldBe(52);
        foreach (var kind in AllKinds)
        {
            var text = PushText.ForNotification(kind, "Rana Haddad", "Villa Haddad", Revealing, 2);
            text.Title.ShouldNotBeNullOrWhiteSpace(kind);
            text.Body.ShouldNotBeNullOrWhiteSpace(kind);
            text.Body.ShouldNotBe("You have a new notification.", kind);
        }
    }

    [Theory]
    [MemberData(nameof(MoneyKinds))]
    public void Money_kinds_never_show_a_client_an_amount_a_margin_or_pay(string kind)
    {
        var text = PushText.ForNotification(kind, "Rana Haddad", "Villa Haddad", Revealing, 3);
        var shown = text.Title + " " + text.Body;

        foreach (var secret in new[] { "Haddad", "INV-", "48250", "48,250", "17.4", "91", "@", "Sami", "615", "Villa", "2026" })
        {
            shown.ShouldNotContain(secret, Case.Sensitive, $"{kind}: {shown}");
        }

        // the same text whatever the notification says
        PushText.ForNotification(kind, null, null, null, 1).ShouldBe(text);
    }

    [Fact]
    public void Money_kinds_get_the_agreed_neutral_lock_screen_texts()
    {
        string Body(string kind) => PushText.ForNotification(kind, "Rana", "Villa Haddad", Revealing, 1).Body;

        AllKinds.Count(k => PushCategories.Of(k) == NotificationCategory.Money).ShouldBe(19);
        Body(NotificationKinds.PaymentRecorded).ShouldBe("Payment recorded. Open ID InDetails to see it.");
        Body(NotificationKinds.PocketRequested).ShouldBe("A site asks for pocket money. Open ID InDetails to decide.");
        Body(NotificationKinds.ChangeBlocked).ShouldBe("A change was stopped to keep a project above zero profit. Open ID InDetails to decide.");
        Body(NotificationKinds.ProfitLow).ShouldBe("A project is close to zero profit.");
        Body(NotificationKinds.ProfitAtZero).ShouldBe("A project is at zero profit or below.");
        Body(NotificationKinds.QuoteDue).ShouldBe("A new project may need a quote. Open ID InDetails to decide.");
        Body(NotificationKinds.InvoiceOverdue).ShouldBe("An invoice needs your attention.");
        Body(NotificationKinds.InvoiceDraftWaiting).ShouldBe("An invoice needs your attention.");
        Body(NotificationKinds.InvoiceReminderDue).ShouldBe("An invoice needs your attention.");
        Body(NotificationKinds.InvoiceSendDue).ShouldBe("An invoice needs your attention.");
        Body(NotificationKinds.InvoiceSendFailed).ShouldBe("An invoice couldn't be sent automatically.");
        Body(NotificationKinds.InvoiceIssued).ShouldBe("Billing update. Open ID InDetails to see it.");
        Body(NotificationKinds.InvoiceSentAutomatically).ShouldBe("Billing update. Open ID InDetails to see it.");
        Body(NotificationKinds.StatementSent).ShouldBe("Billing update. Open ID InDetails to see it.");
        Body(NotificationKinds.ProjectClosed).ShouldBe("A project's money needs a look.");
        Body(NotificationKinds.ProjectCostHigh).ShouldBe("A project's money needs a look.");
        Body(NotificationKinds.ClientAdded).ShouldBe("A new client was added.");
        Body(NotificationKinds.PayChanged).ShouldBe("Pay and hours update.");
        Body(NotificationKinds.HoursShort).ShouldBe("Pay and hours update.");
    }

    [Fact]
    public void Other_kinds_name_the_project_and_who_acted()
    {
        PushText.ForNotification(NotificationKinds.RequirementAssigned, "Rana", "Villa Haddad", null, 1)
            .ShouldBe(new PushMessageText("New requirement", "Rana assigned you a requirement on Villa Haddad."));
        PushText.ForNotification(NotificationKinds.MaterialsAdded, "Sami", "Tower B", JsonDocument.Parse("""{"count":4}""").RootElement, 1)
            .Body.ShouldBe("Sami added 4 materials on Tower B.");
        PushText.ForNotification(NotificationKinds.LabourWeekSubmitted, "Sami", "Tower B",
                JsonDocument.Parse("""{"weekStart":"2026-10-05","updated":true}""").RootElement, 1)
            .Body.ShouldBe("Sami updated the labour week of 5 Oct on Tower B.");
        PushText.ForNotification(NotificationKinds.ProjectStatusChanged, "Rana", "Tower B", JsonDocument.Parse("""{"change":"hold"}""").RootElement, 1)
            .Body.ShouldBe("Tower B was put on hold.");
        PushText.ForNotification(NotificationKinds.RequirementsAdded, "Rana", "Tower B", null, 3)
            .Body.ShouldBe("3 requirements were added to Tower B today.");
        PushText.ForNotification(NotificationKinds.RoleChanged, "Rana", null, JsonDocument.Parse("""{"to":"admin"}""").RootElement, 1)
            .Body.ShouldBe("You're now an admin.");
    }

    [Fact]
    public void A_file_comment_names_who_which_file_and_where_but_never_the_text()
    {
        var payload = JsonDocument.Parse("""{"projectTitle":"Villa Haddad","fileTitle":"Living room","authorName":"Rana Haddad","text":"Darker sofa?"}""").RootElement;

        PushText.ForNotification(NotificationKinds.ProjectFileComment, "Rana Haddad", "Villa Haddad", payload, 2)
            .ShouldBe(new PushMessageText("File comment", "Rana Haddad commented on Living room in Villa Haddad."));
        PushText.ForNotification(NotificationKinds.ProjectFileComment, null, null, null, 1).Body.ShouldBe("Someone commented on a file in a project.");
        PushCategories.Of(NotificationKinds.ProjectFileComment).ShouldBe(NotificationCategory.Messages);
        PushTarget.ForNotification(NotificationKinds.ProjectFileComment, 7, DomainCodes.Design, "item", 12, payload).ShouldBe(PushTarget.Notifications);
    }

    [Fact]
    public void Missing_names_read_naturally()
        => PushText.ForNotification(NotificationKinds.WorkLogged, null, " ", null, 1).Body.ShouldBe("Someone logged work on a project.");

    [Fact]
    public void A_message_shows_its_sender_and_a_one_line_preview()
    {
        PushText.ForMessage("Rana", null, "Can you send\n\nthe  drawings?", MessageKinds.Text, false, false)
            .ShouldBe(new PushMessageText("Rana", "Can you send the drawings?"));
        PushText.ForMessage("Rana", "Site team", "On my way", MessageKinds.Text, false, mentioned: false).Title.ShouldBe("Rana in Site team");
        PushText.ForMessage("Rana", "Site team", "@Sami look", MessageKinds.Text, false, mentioned: true).Title.ShouldBe("Rana mentioned you in Site team");
        PushText.ForMessage("Rana", "Site team", "Fix the joint", MessageKinds.TaskComment, false, true).Title.ShouldBe("Rana commented on a task in Site team");
        PushText.ForMessage("Rana", null, null, MessageKinds.Text, hasAttachments: true, false).Body.ShouldBe("Sent a photo or video.");
    }

    [Fact]
    public void A_long_message_is_cut_to_100_characters()
    {
        var preview = PushText.Preview(new string('a', 300))!;

        preview.Length.ShouldBe(PushText.MaxPreviewLength);
        preview.ShouldEndWith("…");
        PushText.Preview("   ").ShouldBeNull();
    }

    [Fact]
    public void Reminders_have_their_own_texts()
    {
        PushText.LogReminder().Title.ShouldBe("Log your hours");
        PushText.LabourWeekReminder("Tower B").Body.ShouldBe("This week's labour on Tower B isn't reported yet.");
    }
}
