using System.Globalization;
using System.Net;
using ID_Indetails.api.Services.Email.Abstraction;

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
        var steps = report.Steps is null ? "" : $"<p><strong>Steps</strong></p><p style=\"white-space:pre-wrap\">{Encode(report.Steps)}</p>";
        var rows = string.Concat(details.Select(d => $"<tr><td style=\"color:#666\">{Encode(d.Label)}</td><td>{Encode(d.Value)}</td></tr>"));
        var html = $"""
            <p><strong>{Encode(report.Title)}</strong></p>
            <p><strong>What happened</strong></p>
            <p style="white-space:pre-wrap">{Encode(report.Description)}</p>
            {steps}
            <table cellpadding="4" style="border-collapse:collapse;font-size:13px">{rows}</table>
            """;

        var textSteps = report.Steps is null ? "" : $"Steps:\n{report.Steps}\n\n";
        var text = $"{report.Title}\n\nWhat happened:\n{report.Description}\n\n{textSteps}{string.Join("\n", details.Select(d => $"{d.Label}: {d.Value}"))}\n";

        return new EmailMessage(toAddress, null, subject, html, text);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
