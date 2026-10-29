using System.ComponentModel;
using System.Text.RegularExpressions;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>
/// The forms Proton can fill for the user to review and save. Passwords, permissions, roles, bank details and uploads
/// are on none. A money field (marked <see cref="ProtonMoneyAttribute"/>) is filled only while Proton's money actions
/// are switched on, and the pay forms (schedule, hour excuse) only while its pay-rule actions are.
/// </summary>
public enum ProtonForm
{
    Client,
    Meeting,
    NewProject,
    EditProject,
    ReportBug,
    Invite,
    Message,
    LogWork,
    Requirement,
    WorkEntryComment,
    ReportOptions,
    Team,
    Profile,
    FileRename,
    FileComment,
    PortalIntro,
    PortalMessage,
    WebsiteProject,
    WebsiteService,
    WebsiteLocation,
    WebsiteSocialLink,
    CompanyDetails,
    CompanyExpense,
    Materials,
    Invoice,
    Payment,
    ProjectExpense,
    LabourWeek,
    Schedule,
    HourExcuse,
}

/// <summary>A money value of a fill: filled only while the Money family is on, on a typed turn, and when the user typed the amount.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProtonMoneyAttribute : Attribute;

public sealed record ClientFill
{
    public string? Name { get; init; }

    public string? Organisation { get; init; }

    public string? Note { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }
}

public sealed record MeetingFill
{
    [Description("The project it is about: an id from find or another tool.")]
    public long? ProjectId { get; init; }

    public DateOnly? Day { get; init; }

    [Description("Minutes after midnight, company time: 9:30 is 570.")]
    public int? StartMinute { get; init; }

    public int? EndMinute { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }
}

/// <summary>A new project's details: never its money (revenue, rates, pay), which the user sets themselves.</summary>
public sealed record NewProjectFill
{
    public string? Title { get; init; }

    [Description("An id from find or another tool.")]
    public long? ClientId { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public DateOnly? TargetDate { get; init; }
}

public sealed record EditProjectFill
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public DateOnly? TargetDate { get; init; }
}

public sealed record ReportBugFill
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Steps { get; init; }
}

/// <summary>An invitation's address, position and team: never its role or pay.</summary>
public sealed record InviteFill
{
    [Description("Only an address the user typed in this conversation.")]
    public string? Email { get; init; }

    public string? Position { get; init; }

    [Description("A team id from find_recipient or another tool.")]
    public long? TeamId { get; init; }
}

/// <summary>The user's own work, as the log form takes it: the time is worked out (and its overtime shown) by the form.</summary>
public sealed record LogWorkFill
{
    [Description("The project: an id from find, list_my_work or another tool.")]
    public long? ProjectId { get; init; }

    public DateOnly? Day { get; init; }

    [Description("Minutes after midnight, company time: 9:30 is 570.")]
    public int? StartMinute { get; init; }

    public int? EndMinute { get; init; }

    public string? Description { get; init; }
}

/// <summary>A requirement: new on a project (<c>project_id</c>), or the one being edited (the fill's id).</summary>
public sealed record RequirementFill
{
    [Description("The project, for a new requirement.")]
    public long? ProjectId { get; init; }

    public string? Text { get; init; }

    public DateOnly? Deadline { get; init; }

    [Description("People to assign: ids from find, of the project's business.")]
    public IReadOnlyList<long>? AssigneeIds { get; init; }
}

/// <summary>A comment on a work entry (the fill's id), in the user's words.</summary>
public sealed record WorkEntryCommentFill
{
    public string? Body { get; init; }

    [Description("Also post it to the person's team chat.")]
    public bool? ShareWithTeam { get; init; }
}

/// <summary>The project report's options (the fill's id is the project): nothing is saved, it downloads.</summary>
public sealed record ReportOptionsFill
{
    [Description("internal (the full report) or client (the client summary).")]
    public ProjectReportKind? Kind { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("csv: the work entries as a spreadsheet instead of the PDF.")]
    public bool? Csv { get; init; }
}

/// <summary>A team's new name (the fill's id is the team): a new team is proposed on a card instead.</summary>
public sealed record TeamFill
{
    public string? Name { get; init; }
}

/// <summary>The user's own name and phone: never their email, password or photo.</summary>
public sealed record ProfileFill
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Phone { get; init; }
}

/// <summary>A project file's new title (the fill's id is the file's item id).</summary>
public sealed record FileRenameFill
{
    public long? ProjectId { get; init; }

    public string? Title { get; init; }
}

/// <summary>A comment for the team on a project file (the fill's id is the file's item id), in the user's words.</summary>
public sealed record FileCommentFill
{
    public long? ProjectId { get; init; }

    public string? Body { get; init; }
}

/// <summary>The client portal's introduction (the fill's id is the project): the client reads it once it is published.</summary>
public sealed record PortalIntroFill
{
    public string? Text { get; init; }
}

/// <summary>A message to the client in a project's discussion (the fill's id is the project), or a comment on one of its files.</summary>
public sealed record PortalMessageFill
{
    [Description("A file's item id, for a comment on it; leave out for the project chat.")]
    public long? ItemId { get; init; }

    public string? Body { get; init; }
}

/// <summary>A website project's details (the fill's id when editing one): never its pictures, which only a person adds.</summary>
public sealed record WebsiteProjectFill
{
    public string? Title { get; init; }

    public string? Type { get; init; }

    public WebsiteProjectStatus? Status { get; init; }

    public int? Year { get; init; }

    public string? Client { get; init; }

    public string? Country { get; init; }

    public string? City { get; init; }

    public string? Description { get; init; }

    public string? ConceptDescription { get; init; }

    [Description("What the page picture shows, for people who can't see it.")]
    public string? PictureAlt { get; init; }
}

public sealed record WebsiteServiceFill
{
    public string? Title { get; init; }

    public string? Description { get; init; }
}

public sealed record WebsiteLocationFill
{
    public string? Country { get; init; }

    public string? City { get; init; }

    public string? Address { get; init; }

    public string? Phone { get; init; }
}

public sealed record WebsiteSocialLinkFill
{
    public string? Label { get; init; }

    [Description("Only an https address the user typed.")]
    public string? Url { get; init; }
}

/// <summary>The company's details: its contact email only when the user typed it.</summary>
public sealed record CompanyDetailsFill
{
    public string? Name { get; init; }

    public string? Website { get; init; }

    public string? Address { get; init; }

    public string? ContactEmail { get; init; }
}

/// <summary>A recurring company expense (the fill's id when editing one).</summary>
public sealed record CompanyExpenseFill
{
    public string? Name { get; init; }

    [Description("A category id from get_company_expenses.")]
    public long? GroupId { get; init; }

    [ProtonMoney]
    public decimal? MonthlyAmount { get; init; }

    [ProtonMoney]
    [Description("USD or LBP.")]
    public string? Currency { get; init; }
}

/// <summary>A site's new expenses (the fill's id is the project), one row each. The row's type is the item.</summary>
public sealed record MaterialsFill
{
    public IReadOnlyList<MaterialRowFill>? Rows { get; init; }
}

public sealed record MaterialRowFill
{
    public string? Type { get; init; }

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    [ProtonMoney]
    public decimal? UnitCost { get; init; }

    public string? Supplier { get; init; }

    public string? Note { get; init; }
}

/// <summary>An invoice draft: new on a project (<c>project_id</c>), or the draft being edited (the fill's id).</summary>
public sealed record InvoiceFill
{
    public long? ProjectId { get; init; }

    public IReadOnlyList<InvoiceLineFill>? Lines { get; init; }

    [Description("Days the client has to pay.")]
    public int? TermsDays { get; init; }

    public string? Note { get; init; }
}

public sealed record InvoiceLineFill
{
    public string? Description { get; init; }

    [ProtonMoney]
    public decimal? Amount { get; init; }
}

/// <summary>A client payment on a project (the fill's id): a money form, filled only while money actions are on.</summary>
public sealed record PaymentFill
{
    [Description("The invoice it pays, from list_invoices; leave out for a payment on account.")]
    public long? InvoiceId { get; init; }

    [ProtonMoney]
    public decimal? Amount { get; init; }

    [ProtonMoney]
    [Description("USD or LBP.")]
    public string? Currency { get; init; }

    public DateOnly? ReceivedOn { get; init; }

    public PaymentMethod? Method { get; init; }

    public string? Reference { get; init; }
}

/// <summary>A project's expense (the fill's id is the project).</summary>
public sealed record ProjectExpenseFill
{
    public string? Label { get; init; }

    public DateOnly? Date { get; init; }

    public string? Category { get; init; }

    [ProtonMoney]
    public decimal? Amount { get; init; }

    [ProtonMoney]
    [Description("USD or LBP.")]
    public string? Currency { get; init; }
}

/// <summary>A site's labour for one week (the fill's id is the project).</summary>
public sealed record LabourWeekFill
{
    [Description("Any day of the week: the form shows its whole week.")]
    public DateOnly? Week { get; init; }

    public IReadOnlyList<LabourDayFill>? Days { get; init; }
}

public sealed record LabourDayFill
{
    public DateOnly? Date { get; init; }

    public int? Workers { get; init; }

    [ProtonMoney]
    public decimal? DailyWage { get; init; }
}

/// <summary>A person's own working week (the fill's id is the person): a pay form, filled only while pay-rule actions are on.</summary>
public sealed record ScheduleFill
{
    [Description("Back to the company's working week.")]
    public bool? FollowsDefault { get; init; }

    [Description("Every working day of the week, with its hours; a day left out is a day off.")]
    public IReadOnlyList<ScheduleDayFill>? Days { get; init; }
}

public sealed record ScheduleDayFill
{
    [Description("0 Sunday to 6 Saturday.")]
    public int? Weekday { get; init; }

    public int? StartMinute { get; init; }

    public int? EndMinute { get; init; }
}

/// <summary>Excused hours or unpaid leave for a person (the fill's id): a pay form, filled only while pay-rule actions are on.</summary>
public sealed record HourExcuseFill
{
    [Description("true: excused and paid; false: unpaid leave.")]
    public bool? Excused { get; init; }

    [Description("Hours to excuse, or leave out and give days.")]
    public decimal? Hours { get; init; }

    public IReadOnlyList<DateOnly>? Days { get; init; }

    public string? Reason { get; init; }
}

public sealed record MessageFill
{
    [Description("The message, in the user's voice, from what the user said: they read it and press Send themselves.")]
    public required string Body { get; init; }
}

/// <summary>
/// What a fill puts in a form: one form's values, typed. The app applies them to empty, visible fields only, marks
/// them, and never submits.
/// </summary>
/// <param name="Id">The record the form is for (the project being edited, the person or team of a chat); the app refuses a form open for another.</param>
public sealed record ProtonFormFill(
    ProtonForm Form,
    long? Id = null,
    ClientFill? Client = null,
    MeetingFill? Meeting = null,
    NewProjectFill? NewProject = null,
    EditProjectFill? EditProject = null,
    ReportBugFill? ReportBug = null,
    InviteFill? Invite = null,
    MessageFill? Message = null,
    LogWorkFill? LogWork = null,
    RequirementFill? Requirement = null,
    WorkEntryCommentFill? WorkEntryComment = null,
    ReportOptionsFill? ReportOptions = null,
    TeamFill? Team = null,
    ProfileFill? Profile = null,
    FileRenameFill? FileRename = null,
    FileCommentFill? FileComment = null,
    PortalIntroFill? PortalIntro = null,
    PortalMessageFill? PortalMessage = null,
    WebsiteProjectFill? WebsiteProject = null,
    WebsiteServiceFill? WebsiteService = null,
    WebsiteLocationFill? WebsiteLocation = null,
    WebsiteSocialLinkFill? WebsiteSocialLink = null,
    CompanyDetailsFill? CompanyDetails = null,
    CompanyExpenseFill? CompanyExpense = null,
    MaterialsFill? Materials = null,
    InvoiceFill? Invoice = null,
    PaymentFill? Payment = null,
    ProjectExpenseFill? ProjectExpense = null,
    LabourWeekFill? LabourWeek = null,
    ScheduleFill? Schedule = null,
    HourExcuseFill? HourExcuse = null)
{
    /// <summary>The one values record the fill gives (for <see cref="Form"/> when it is well formed).</summary>
    public object? Values => new object?[]
    {
        Client, Meeting, NewProject, EditProject, ReportBug, Invite, Message, LogWork, Requirement, WorkEntryComment, ReportOptions,
        Team, Profile, FileRename, FileComment, PortalIntro, PortalMessage, WebsiteProject, WebsiteService, WebsiteLocation,
        WebsiteSocialLink, CompanyDetails, CompanyExpense, Materials, Invoice, Payment, ProjectExpense, LabourWeek, Schedule, HourExcuse,
    }.SingleOrDefault(v => v is not null);
}

/// <summary>The shape rules of a fill, before the app sees it: field-level messages go back to the model to correct.</summary>
public static partial class ProtonFormRules
{
    public const int NameLength = 200;
    public const int TextLength = 2_000;
    public const int MessageLength = 4_000;
    public const int MaxRows = 50;

    /// <summary>The problems with a fill's values, in words; empty when it may go to the app.</summary>
    public static IReadOnlyList<string> Problems(ProtonFormFill fill)
    {
        var problems = new List<string>();
        void Text(string field, string? value, int max)
        {
            if (value is not null && (value.Trim().Length == 0 || value.Length > max))
            {
                problems.Add($"{field}: leave it out, or give 1 to {max} characters.");
            }
        }

        void Needs(string what, long? id)
        {
            if (id is not > 0)
            {
                problems.Add($"Give {what}.");
            }
        }

        void Rows(string field, int? count, int max, bool required = false)
        {
            if (count > max || required && count is null or 0)
            {
                problems.Add($"{field}: {(required ? "1" : "0")} to {max}.");
            }
        }

        void Money(string field, decimal? value)
        {
            if (value is < 0 or > 100_000_000)
            {
                problems.Add($"{field}: 0 or more, within reason.");
            }
        }

        void Currency(string? value)
        {
            if (value is not (null or "USD" or "LBP"))
            {
                problems.Add("currency: USD or LBP.");
            }
        }

        switch (fill.Form)
        {
            case ProtonForm.Client when fill.Client is { } c:
                Text("name", c.Name, NameLength);
                Text("organisation", c.Organisation, NameLength);
                Text("note", c.Note, TextLength);
                Text("phone", c.Phone, 40);
                if (c.Email is { } email && !IsEmail(email))
                {
                    problems.Add("email: not an email address.");
                }

                break;
            case ProtonForm.Meeting when fill.Meeting is { } m:
                Text("topic", m.Topic, NameLength);
                Text("description", m.Description, TextLength);
                if (m.StartMinute is < 0 or > 1439 || m.EndMinute is < 0 or > 1439)
                {
                    problems.Add("start_minute and end_minute: minutes after midnight, 0 to 1439.");
                }
                else if (m.StartMinute is { } start && m.EndMinute is { } end && end <= start)
                {
                    problems.Add("end_minute: after the start.");
                }

                break;
            case ProtonForm.NewProject when fill.NewProject is { } p:
                Text("title", p.Title, NameLength);
                Text("description", p.Description, TextLength);
                Text("location", p.Location, NameLength);
                break;
            case ProtonForm.EditProject when fill.EditProject is { } p:
                Text("title", p.Title, NameLength);
                Text("description", p.Description, TextLength);
                Text("location", p.Location, NameLength);
                break;
            case ProtonForm.ReportBug when fill.ReportBug is { } b:
                Text("title", b.Title, NameLength);
                Text("description", b.Description, TextLength);
                Text("steps", b.Steps, TextLength);
                break;
            case ProtonForm.Invite when fill.Invite is { } i:
                Text("position", i.Position, NameLength);
                if (i.Email is { } address && !IsEmail(address))
                {
                    problems.Add("email: not an email address.");
                }

                break;
            case ProtonForm.Message when fill.Message is { } msg:
                Text("body", msg.Body, MessageLength);
                break;
            case ProtonForm.LogWork when fill.LogWork is { } w:
                Text("description", w.Description, TextLength);
                if (w.StartMinute is < 0 or > 1439 || w.EndMinute is < 0 or > 1439)
                {
                    problems.Add("start_minute and end_minute: minutes after midnight, 0 to 1439.");
                }

                break;
            case ProtonForm.Requirement when fill.Requirement is { } r:
                Text("text", r.Text, TextLength);
                Rows("assignee_ids", r.AssigneeIds?.Count, 20);
                if (fill.Id is null == r.ProjectId is null)
                {
                    problems.Add("Give project_id for a new requirement, or the requirement's id to edit one.");
                }

                break;
            case ProtonForm.WorkEntryComment when fill.WorkEntryComment is { } c:
                Text("body", c.Body, TextLength);
                Needs("the work entry's id", fill.Id);
                break;
            case ProtonForm.ReportOptions when fill.ReportOptions is { } o:
                Needs("the project's id", fill.Id);
                if (o.From is { } from && o.To is { } to && to < from)
                {
                    problems.Add("to: on or after from.");
                }

                break;
            case ProtonForm.Team when fill.Team is { } t:
                Text("name", t.Name, NameLength);
                Needs("the team's id (a new team is proposed with propose_create_team)", fill.Id);
                break;
            case ProtonForm.Profile when fill.Profile is { } me:
                Text("first_name", me.FirstName, NameLength);
                Text("last_name", me.LastName, NameLength);
                Text("phone", me.Phone, 40);
                break;
            case ProtonForm.FileRename when fill.FileRename is { } f:
                Text("title", f.Title, NameLength);
                Needs("project_id", f.ProjectId);
                Needs("the file's item id", fill.Id);
                break;
            case ProtonForm.FileComment when fill.FileComment is { } f:
                Text("body", f.Body, TextLength);
                Needs("project_id", f.ProjectId);
                Needs("the file's item id", fill.Id);
                break;
            case ProtonForm.PortalIntro when fill.PortalIntro is { } i:
                Text("text", i.Text, TextLength);
                Needs("the project's id", fill.Id);
                break;
            case ProtonForm.PortalMessage when fill.PortalMessage is { } m:
                Text("body", m.Body, m.ItemId is null ? MessageLength : TextLength);
                Needs("the project's id", fill.Id);
                break;
            case ProtonForm.WebsiteProject when fill.WebsiteProject is { } wp:
                Text("title", wp.Title, NameLength);
                Text("type", wp.Type, 60);
                Text("client", wp.Client, NameLength);
                Text("country", wp.Country, 100);
                Text("city", wp.City, 100);
                Text("description", wp.Description, TextLength);
                Text("concept_description", wp.ConceptDescription, TextLength);
                Text("picture_alt", wp.PictureAlt, NameLength);
                if (wp.Year is < 1900 or > 2100)
                {
                    problems.Add("year: between 1900 and 2100.");
                }

                break;
            case ProtonForm.WebsiteService when fill.WebsiteService is { } ws:
                Text("title", ws.Title, NameLength);
                Text("description", ws.Description, TextLength);
                break;
            case ProtonForm.WebsiteLocation when fill.WebsiteLocation is { } wl:
                Text("country", wl.Country, 100);
                Text("city", wl.City, 100);
                Text("address", wl.Address, NameLength);
                Text("phone", wl.Phone, 40);
                break;
            case ProtonForm.WebsiteSocialLink when fill.WebsiteSocialLink is { } link:
                Text("label", link.Label, 60);
                if (link.Url is { } url && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && url.Length <= 500))
                {
                    problems.Add("url: a full https:// address.");
                }

                break;
            case ProtonForm.CompanyDetails when fill.CompanyDetails is { } cd:
                Text("name", cd.Name, NameLength);
                Text("website", cd.Website, NameLength);
                Text("address", cd.Address, NameLength);
                if (cd.ContactEmail is { } contact && !IsEmail(contact))
                {
                    problems.Add("contact_email: not an email address.");
                }

                break;
            case ProtonForm.CompanyExpense when fill.CompanyExpense is { } ce:
                Text("name", ce.Name, NameLength);
                Money("monthly_amount", ce.MonthlyAmount);
                Currency(ce.Currency);
                break;
            case ProtonForm.Materials when fill.Materials is { } mat:
                Needs("the project's id", fill.Id);
                Rows("rows", mat.Rows?.Count, MaxRows, required: true);
                foreach (var row in mat.Rows ?? [])
                {
                    Text("type", row.Type, NameLength);
                    Text("unit", row.Unit, 40);
                    Text("supplier", row.Supplier, NameLength);
                    Text("note", row.Note, TextLength);
                    Money("quantity", row.Quantity);
                    Money("unit_cost", row.UnitCost);
                }

                break;
            case ProtonForm.Invoice when fill.Invoice is { } inv:
                Text("note", inv.Note, TextLength);
                Rows("lines", inv.Lines?.Count, MaxRows);
                foreach (var line in inv.Lines ?? [])
                {
                    Text("description", line.Description, NameLength);
                    Money("amount", line.Amount);
                }

                if (inv.TermsDays is < 0 or > 365)
                {
                    problems.Add("terms_days: 0 to 365.");
                }

                if (fill.Id is null == inv.ProjectId is null)
                {
                    problems.Add("Give project_id for a new invoice, or the draft's id to edit one.");
                }

                break;
            case ProtonForm.Payment when fill.Payment is { } pay:
                Needs("the project's id", fill.Id);
                Money("amount", pay.Amount);
                Currency(pay.Currency);
                Text("reference", pay.Reference, NameLength);
                break;
            case ProtonForm.ProjectExpense when fill.ProjectExpense is { } ex:
                Needs("the project's id", fill.Id);
                Text("label", ex.Label, NameLength);
                Text("category", ex.Category, 60);
                Money("amount", ex.Amount);
                Currency(ex.Currency);
                break;
            case ProtonForm.LabourWeek when fill.LabourWeek is { } lw:
                Needs("the project's id", fill.Id);
                Rows("days", lw.Days?.Count, 7, required: true);
                foreach (var day in lw.Days ?? [])
                {
                    if (day.Workers is < 0 or > 1_000)
                    {
                        problems.Add("workers: 0 to 1000.");
                    }

                    Money("daily_wage", day.DailyWage);
                }

                break;
            case ProtonForm.Schedule when fill.Schedule is { } sch:
                Needs("the person's id", fill.Id);
                Rows("days", sch.Days?.Count, 7);
                foreach (var day in sch.Days ?? [])
                {
                    if (day.Weekday is not (>= 0 and <= 6))
                    {
                        problems.Add("weekday: 0 (Sunday) to 6 (Saturday).");
                    }

                    if (day.StartMinute is not (>= 0 and <= 1439) || day.EndMinute is not (>= 0 and <= 1439) || day.EndMinute <= day.StartMinute)
                    {
                        problems.Add("start_minute and end_minute: minutes after midnight, 0 to 1439, the end after the start.");
                    }
                }

                break;
            case ProtonForm.HourExcuse when fill.HourExcuse is { } he:
                Needs("the person's id", fill.Id);
                Text("reason", he.Reason, NameLength);
                if (he.Hours is not null == he.Days is { Count: > 0 })
                {
                    problems.Add("Give hours, or days, not both.");
                }

                if (he.Hours is <= 0 or > 300)
                {
                    problems.Add("hours: more than 0, at most 300.");
                }

                Rows("days", he.Days?.Count, 31);
                break;
            default:
                problems.Add($"Give the values for the {fill.Form} form, and only those.");
                break;
        }

        return problems;
    }

    public static bool IsEmail(string value) => value.Length <= 254 && EmailPattern().IsMatch(value);

    /// <summary>The money values a fill gives (by their fields), for the Money family and the typed-amount check.</summary>
    public static IReadOnlyList<(string Field, decimal Value)> MoneyIn(object? values)
    {
        var found = new List<(string, decimal)>();
        void Walk(object? value)
        {
            if (value is null)
            {
                return;
            }

            if (value is System.Collections.IEnumerable list and not string)
            {
                foreach (var item in list)
                {
                    Walk(item);
                }

                return;
            }

            foreach (var property in value.GetType().GetProperties())
            {
                var current = property.GetValue(value);
                if (property.IsDefined(typeof(ProtonMoneyAttribute), false))
                {
                    if (current is decimal amount)
                    {
                        found.Add((property.Name, amount));
                    }
                }
                else if (current is not null && current.GetType().Namespace == typeof(ProtonFormFill).Namespace)
                {
                    Walk(current);
                }
                else if (current is System.Collections.IEnumerable rows and not string)
                {
                    Walk(rows);
                }
            }
        }

        Walk(values);
        return found;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
