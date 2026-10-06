using System.Globalization;
using ID_Indetails.api.Services.Email.Abstraction;
using static ID_Indetails.api.Services.Email.Implementation.EmailLayout;

namespace ID_Indetails.api.Services.Email.Implementation;

/// <summary>A bug report's details: what the reporter wrote, and what the server knows about them.</summary>
public sealed record BugReport(
    string Title,
    string Description,
    string? Steps,
    string? Page,
    long ReporterId,
    string ReporterName,
    string ReporterEmail,
    string Role,
    string? Domain,
    string? UserAgent,
    DateTime ReportedAtUtc);

/// <summary>Content of the emails sent to the people who support the product. Every user-supplied value is HTML-encoded.</summary>
public static class SupportEmails
{
    public static EmailMessage BugReport(string productName, string toAddress, BugReport report)
    {
        var details = new (string Label, string Value)[]
        {
            ("Reported by", $"{report.ReporterName} (user {report.ReporterId}, {report.ReporterEmail})"),
            ("Role", $"{report.Role} · {report.Domain ?? "company"}"),
            ("Page", report.Page ?? "not given"),
            ("When", report.ReportedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)),
            ("Browser", report.UserAgent ?? "not given"),
        };
        var subject = $"{productName} bug report: {report.Title}";
        var html = Title(report.Title)
            + Heading("What happened")
            + Quote(report.Description)
            + (report.Steps is null ? "" : Heading("Steps") + Quote(report.Steps))
            + Heading("Details")
            + Table(["Detail", "Value"], details.Select(d => new[] { d.Label, d.Value }));

        var textSteps = report.Steps is null ? "" : $"Steps:\n{report.Steps}\n\n";
        var text = $"{report.Title}\n\nWhat happened:\n{report.Description}\n\n{textSteps}{string.Join("\n", details.Select(d => $"{d.Label}: {d.Value}"))}\n";

        return Compose(toAddress, null, subject, Page(productName, $"Bug report: {report.Title}", html, "Sent from the Report a bug window."), text);
    }
}
