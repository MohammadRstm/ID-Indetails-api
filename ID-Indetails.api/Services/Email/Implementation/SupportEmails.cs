using System.Globalization;
using System.Text.RegularExpressions;
using ID_Indetails.api.Services.Email.Abstraction;
using static ID_Indetails.api.Services.Email.Implementation.EmailLayout;

namespace ID_Indetails.api.Services.Email.Implementation;

/// <summary>Who sent a message to the developer, as the server knows them.</summary>
/// <param name="Account">Which account: "user 12" for staff, "portal account 7" for a client.</param>
/// <param name="Role">Their role and business ("employee · design"), or that they are a client.</param>
public sealed record ContactSender(string Name, string Email, string Account, string Role);

/// <summary>A message to the developer: what the sender wrote, and what the server knows about them.</summary>
public sealed record BugReport(
    string Description,
    string? Details,
    string? Page,
    ContactSender Sender,
    string? UserAgent,
    bool? InstalledApp,
    StampedTime SentAt,
    ContactError? Error = null,
    IReadOnlyList<ReportedError>? RecentErrors = null);

/// <summary>A moment in UTC, as the API log has it, and in the company's time zone, as the person saw it.</summary>
public sealed record StampedTime(DateTime Utc, DateTime Company);

/// <summary>The error a message is about: an API error's trace id, or the browser's message for a page that failed.</summary>
public sealed record ContactError(string? Ref, StampedTime At, string? Message);

/// <summary>A request that failed lately in the sender's browser: its trace id finds its lines in the log.</summary>
public sealed record ReportedError(string Ref, int Status, string? Path, DateTime AtUtc);

/// <summary>Content of the emails sent to the developer, and of the sender's copy. Every user-supplied value is HTML-encoded.</summary>
public static partial class SupportEmails
{
    public static EmailMessage BugReport(string productName, string toAddress, BugReport report)
    {
        var sender = report.Sender;
        var details = new (string Label, string? Value)[]
        {
            ("From", $"{sender.Name} ({sender.Account}, {sender.Email})"),
            ("Role", sender.Role),
            ("Page", report.Page ?? "not given"),
            ("Sent", Full(report.SentAt)),
            ("Opened in", report.InstalledApp switch { true => "the installed app", false => "a browser tab", null => null }),
            ("Browser", report.UserAgent ?? "not given"),
        }.Where(d => d.Value is not null).Select(d => (d.Label, Value: d.Value!)).ToList();
        var error = ErrorFacts(report.Error);

        var html = Title($"Message from {sender.Name}")
            + Heading("What they were trying to do, and what happened")
            + Quote(report.Description)
            + (report.Details is null ? "" : Heading("Anything else") + Quote(report.Details))
            + (error.Count == 0 ? "" : Heading("The error they came from") + Table(["Detail", "Value"], error.Select(e => new[] { e.Label, e.Value })))
            + Heading("Details")
            + Table(["Detail", "Value"], details.Select(d => new[] { d.Label, d.Value }));

        // the trace ids find the failed requests' lines in the API log
        var recent = (report.RecentErrors ?? [])
            .Select(e => new[] { e.Ref, e.Status.ToString(CultureInfo.InvariantCulture), e.Path ?? "", Utc(e.AtUtc) })
            .ToList();
        if (recent.Count > 0)
        {
            html += Heading("Recent errors in their browser") + Table(["Reference", "Status", "Request", "When"], recent);
        }

        var text = $"Message from {sender.Name}\n\nWhat they were trying to do, and what happened:\n{report.Description}\n\n"
            + (report.Details is null ? "" : $"Anything else:\n{report.Details}\n\n")
            + (error.Count == 0 ? "" : "The error they came from:\n" + Lines(error) + "\n\n")
            + Lines(details) + "\n"
            + (recent.Count == 0 ? "" : "\nRecent errors:\n" + string.Join("\n", recent.Select(e => string.Join(" · ", e))) + "\n");

        var subject = $"{productName}: message from {sender.Name}" + report.Error switch
        {
            { Ref: { } reference } => $" (ref {RefOf(reference)})",
            not null => " (a page failed)",
            null => "",
        };
        var message = Compose(toAddress, null, subject, Page(productName, $"Message from {sender.Name}", html, "Sent from Contact the developer."), text);
        // Reply in the mail app answers the sender
        return message with { ReplyToAddress = sender.Email, ReplyToName = sender.Name };
    }

    /// <summary>The sender's copy: their own words and the error's reference, none of what the server adds about them.</summary>
    public static EmailMessage BugReportCopy(string productName, BugReport report)
    {
        var reference = report.Error?.Ref is { } r ? RefOf(r) : null;
        var facts = new (string Label, string? Value)[]
        {
            ("Error reference", reference),
            ("When it happened", report.Error is { } e ? Local(e.At) : null),
            ("Sent", Local(report.SentAt)),
        };
        var html = Title("Your message to the developer")
            + Paragraph("Here is a copy of what you sent. If the developer replies, it comes to this address.")
            + Heading("What you were trying to do, and what happened")
            + Quote(report.Description)
            + (report.Details is null ? "" : Heading("Anything else") + Quote(report.Details))
            + Facts(facts);
        var text = $"Your message to the developer\n\nHere is a copy of what you sent.\n\nWhat you were trying to do, and what happened:\n{report.Description}\n\n"
            + (report.Details is null ? "" : $"Anything else:\n{report.Details}\n\n")
            + string.Join("\n", facts.Where(f => f.Value is not null).Select(f => $"{f.Label}: {f.Value}")) + "\n";

        return Compose(report.Sender.Email, report.Sender.Name, $"{productName}: your message to the developer",
            Page(productName, "A copy of your message to the developer", html, $"Sent because you used Contact the developer in {productName}."), text);
    }

    /// <summary>The part of a trace id a person reads out, as the app shows it: the first 8 characters of a W3C trace id, otherwise the whole id.</summary>
    public static string RefOf(string traceId)
    {
        var w3c = W3CTraceId().Match(traceId);
        return w3c.Success ? w3c.Groups[1].Value[..8] : traceId;
    }

    private static List<(string Label, string Value)> ErrorFacts(ContactError? error)
    {
        if (error is null)
        {
            return [];
        }

        var facts = new List<(string Label, string Value)>();
        if (error.Ref is { } reference)
        {
            facts.Add(("Reference", reference));
            if (RefOf(reference) != reference)
            {
                facts.Add(("Search the log for", RefOf(reference)));
            }
        }

        facts.Add(("When", Full(error.At)));
        if (error.Message is { } message)
        {
            facts.Add(("The browser's message", message));
        }

        return facts;
    }

    private static string Lines(IEnumerable<(string Label, string Value)> rows) => string.Join("\n", rows.Select(r => $"{r.Label}: {r.Value}"));

    private static string Utc(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string Full(StampedTime time)
        => $"{Utc(time.Utc)} ({time.Company.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} company time)";

    private static string Local(StampedTime time) => time.Company.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[0-9a-f]{2}-([0-9a-f]{32})-")]
    private static partial Regex W3CTraceId();
}
