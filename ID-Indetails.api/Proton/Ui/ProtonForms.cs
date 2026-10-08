using System.ComponentModel;
using System.Text.RegularExpressions;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>The forms Proton can fill for the user to review and save. Money, pay, passwords and permissions are on none.</summary>
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
}

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

/// <summary>A new project's details: never its money (revenue, fee, markup, pay), which the user sets themselves.</summary>
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
    LogWorkFill? LogWork = null);

/// <summary>The shape rules of a fill, before the app sees it: field-level messages go back to the model to correct.</summary>
public static partial class ProtonFormRules
{
    public const int NameLength = 200;
    public const int TextLength = 2_000;
    public const int MessageLength = 4_000;

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
            default:
                problems.Add($"Give the values for the {fill.Form} form, and only those.");
                break;
        }

        return problems;
    }

    public static bool IsEmail(string value) => value.Length <= 254 && EmailPattern().IsMatch(value);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
