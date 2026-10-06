using System.Net;
using ID_Indetails.api.Services.Email.Abstraction;

namespace ID_Indetails.api.Services.Email.Implementation;

/// <summary>
/// The frame every email is drawn in: the logo, a white card and a footer. Mail apps ignore style sheets and scripts,
/// so it is built from tables with inline styles. The pieces take plain text and encode it; only <c>Html</c>
/// arguments are taken as markup, and callers build those from these pieces.
/// </summary>
public static class EmailLayout
{
    public const string Ink = "#111A1A";
    public const string Muted = "#5E6B6B";
    public const string Line = "#E3E6E6";
    private const string Ground = "#F3F4F4";
    private const string Soft = "#F6F7F7";
    private const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    private const string LogoId = "logo@id-indetails";

    private static readonly Lazy<byte[]> LogoBytes = new(() =>
    {
        using var stream = typeof(EmailLayout).Assembly.GetManifestResourceStream("email-logo.png")
            ?? throw new InvalidOperationException("The email logo isn't built into the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    });

    /// <summary>An email in the frame, carrying the logo it shows.</summary>
    public static EmailMessage Compose(
        string to, string? toName, string subject, string html, string text, IReadOnlyList<EmailAttachment>? attachments = null)
        => new(to, toName, subject, html, text, attachments, [new EmailInlineImage(LogoId, "image/png", LogoBytes.Value)]);

    /// <param name="brand">The name beside the logo.</param>
    /// <param name="preview">The line a mail app shows under the subject in the inbox.</param>
    /// <param name="content">The card's markup, from the pieces below.</param>
    /// <param name="footer">Small lines under the card (the company's name and address, why this email was sent).</param>
    public static string Page(string brand, string preview, string content, params string?[] footer)
    {
        var footerLines = string.Concat(footer
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => $"""<p style="margin:0 0 4px;font-size:12px;line-height:18px;color:{Muted}">{Encode(l!)}</p>"""));
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light">
            <title>{Encode(brand)}</title>
            </head>
            <body style="margin:0;padding:0;background:{Ground};">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{Encode(preview)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{Ground};">
              <tr><td align="center" style="padding:28px 12px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;font-family:{Font};color:{Ink};">
                  <tr><td style="padding:0 4px 18px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                      <td style="padding-right:10px;"><img src="cid:{LogoId}" width="36" height="36" alt="" style="display:block;border:0;border-radius:8px;"></td>
                      <td style="font-size:16px;font-weight:700;letter-spacing:-0.01em;color:{Ink};">{Encode(brand)}</td>
                    </tr></table>
                  </td></tr>
                  <tr><td style="background:#FFFFFF;border:1px solid {Line};border-radius:12px;padding:32px 32px 28px;">
                    {content}
                  </td></tr>
                  <tr><td style="padding:18px 4px 0;">{footerLines}</td></tr>
                </table>
              </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    public static string Title(string text)
        => $"""<h1 style="margin:0 0 16px;font-size:21px;line-height:28px;font-weight:700;letter-spacing:-0.015em;color:{Ink};">{Encode(text)}</h1>""";

    public static string Paragraph(string text)
        => $"""<p style="margin:0 0 14px;font-size:15px;line-height:23px;color:{Ink};">{Encode(text)}</p>""";

    /// <summary>A paragraph of muted, smaller text: a note, a reassurance.</summary>
    public static string Note(string text)
        => $"""<p style="margin:0 0 12px;font-size:13px;line-height:20px;color:{Muted};">{Encode(text)}</p>""";

    public static string Heading(string text)
        => $"""<p style="margin:26px 0 10px;font-size:11px;line-height:16px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:{Muted};">{Encode(text)}</p>""";

    /// <summary>A button that every mail app draws (a link padded inside a table cell), with the link under it.</summary>
    public static string Button(string url, string label)
        => $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:22px 0 10px;"><tr>
              <td style="background:{Ink};border-radius:8px;">
                <a href="{Encode(url)}" style="display:inline-block;padding:13px 24px;font-size:15px;font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:8px;">{Encode(label)}</a>
              </td>
            </tr></table>
            <p style="margin:0 0 18px;font-size:12px;line-height:18px;color:{Muted};word-break:break-all;">Or paste this link into your browser: <a href="{Encode(url)}" style="color:{Muted};">{Encode(url)}</a></p>
            """;

    /// <summary>A highlighted figure: a label, the figure, and a line under it.</summary>
    public static string Highlight(string label, string value, string? sub = null, string? badge = null)
    {
        var badgeHtml = badge is null ? "" : $"""<span style="display:inline-block;margin-left:8px;padding:2px 8px;border-radius:99px;background:#FDE8E4;color:#B4321E;font-size:11px;font-weight:700;vertical-align:middle;letter-spacing:0.02em;">{Encode(badge)}</span>""";
        var subHtml = sub is null ? "" : $"""<div style="margin-top:4px;font-size:13px;color:{Muted};">{Encode(sub)}</div>""";
        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:6px 0 8px;background:{Soft};border:1px solid {Line};border-radius:10px;">
              <tr><td style="padding:18px 20px;">
                <div style="font-size:11px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:{Muted};">{Encode(label)}{badgeHtml}</div>
                <div style="margin-top:6px;font-size:28px;line-height:34px;font-weight:700;letter-spacing:-0.02em;color:{Ink};">{Encode(value)}</div>
                {subHtml}
              </td></tr>
            </table>
            """;
    }

    /// <summary>Labels and their values, two to a row.</summary>
    public static string Facts(params (string Label, string? Value)[] facts)
    {
        var shown = facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToList();
        var rows = new List<string>();
        for (var i = 0; i < shown.Count; i += 2)
        {
            rows.Add("<tr>" + Fact(shown[i]) + (i + 1 < shown.Count ? Fact(shown[i + 1]) : "<td></td>") + "</tr>");
        }

        return $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:14px 0 4px;">{string.Concat(rows)}</table>""";

        static string Fact((string Label, string? Value) f)
            => $"""<td width="50%" valign="top" style="padding:0 12px 12px 0;"><div style="font-size:12px;color:{Muted};">{Encode(f.Label)}</div><div style="margin-top:2px;font-size:14px;font-weight:600;color:{Ink};">{Encode(f.Value!)}</div></td>""";
    }

    /// <summary>A table: its headers, then rows of cells; <paramref name="right"/> says which columns hold amounts.</summary>
    public static string Table(string[] headers, IEnumerable<string[]> rows, params int[] right)
    {
        string Align(int i) => right.Contains(i) ? "right" : "left";
        var head = string.Concat(headers.Select((h, i) =>
            $"""<th align="{Align(i)}" style="padding:8px 10px;font-size:11px;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:{Muted};background:{Soft};border-bottom:1px solid {Line};">{Encode(h)}</th>"""));
        var body = string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select((c, i) =>
            $"""<td align="{Align(i)}" valign="top" style="padding:10px;font-size:14px;line-height:20px;color:{Ink};border-bottom:1px solid {Line};{(right.Contains(i) ? "white-space:nowrap;" : "")}">{Encode(c)}</td>""")) + "</tr>"));
        return $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border:1px solid {Line};border-radius:8px;border-collapse:separate;overflow:hidden;"><tr>{head}</tr>{body}</table>""";
    }

    /// <summary>Totals under a table, right-aligned; a strong row is the one that matters.</summary>
    public static string Totals(params (string Label, string Value, bool Strong)[] rows)
    {
        var html = string.Concat(rows.Select(r =>
        {
            var weight = r.Strong ? "700" : "400";
            var size = r.Strong ? "16px" : "14px";
            var border = r.Strong ? $"border-top:1px solid {Line};" : "";
            return $"""<tr><td align="right" style="padding:7px 10px;font-size:{size};font-weight:{weight};color:{(r.Strong ? Ink : Muted)};{border}">{Encode(r.Label)}</td><td align="right" width="140" style="padding:7px 10px;font-size:{size};font-weight:{weight};color:{Ink};white-space:nowrap;{border}">{Encode(r.Value)}</td></tr>""";
        }));
        return $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:6px 0 4px;">{html}</table>""";
    }

    /// <summary>Text kept as typed (line breaks too), in a soft box.</summary>
    public static string Quote(string text)
        => $"""<div style="margin:0 0 14px;padding:12px 14px;background:{Soft};border-left:3px solid {Line};border-radius:6px;font-size:14px;line-height:21px;color:{Ink};white-space:pre-wrap;">{Encode(text)}</div>""";

    /// <summary>Something to type in, like a temporary password: large, and selected whole by one click where the mail app allows.</summary>
    public static string Code(string text)
        => $"""<div style="margin:6px 0 16px;padding:14px 16px;background:{Soft};border:1px dashed #C9CFCF;border-radius:8px;font-family:Consolas,Menlo,monospace;font-size:20px;letter-spacing:0.06em;color:{Ink};user-select:all;-webkit-user-select:all;">{Encode(text)}</div>""";

    /// <summary>The sign-off: a line, then who it is from and, under them, where they work.</summary>
    public static string SignOff(string line, string from, string? company = null)
    {
        var companyHtml = company is null || company == from ? "" : $"""<br><span style="color:{Muted};">{Encode(company)}</span>""";
        return $"""<p style="margin:24px 0 0;font-size:15px;line-height:23px;color:{Ink};">{Encode(line)}<br><strong>{Encode(from)}</strong>{companyHtml}</p>""";
    }

    public static string Encode(string value) => WebUtility.HtmlEncode(value);
}
