using System.Globalization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>Everything a project report draws on, loaded by the document service.</summary>
/// <param name="Sections">Already narrowed to what this kind and project may show.</param>
/// <param name="Work">Its contributors are those of the range.</param>
/// <param name="Log">The range's most recent entries, newest first.</param>
/// <param name="ToCompanyTime">Turns the responses' UTC instants into company time.</param>
public sealed record ProjectReport(
    ProjectReportKind Kind,
    IReadOnlySet<ProjectReportSection> Sections,
    DateOnly From,
    DateOnly To,
    DateOnly Today,
    DateOnly? CompletedOn,
    ProjectResponse Project,
    ProjectWorkResponse Work,
    ProjectTimelineResponse? Timeline,
    ExpenseListResponse? Expenses,
    IReadOnlyList<InvoiceSummaryResponse> Invoices,
    IReadOnlyList<PaymentResponse> Payments,
    ClientResponse? Client,
    IReadOnlyList<TimelineEntryResponse> Log,
    int LogTotal,
    Func<DateTime, DateTime> ToCompanyTime);

/// <summary>
/// The project report. The internal one has everything; the client summary has hours, the timeline and billing, and
/// never rates, labour cost, expenses, profit or margin. Neither is an invoice.
/// </summary>
public static class ProjectReportPdf
{
    public const int LogRows = 12;

    public static byte[] Render(ProjectReport report, Letterhead head)
    {
        var project = report.Project;
        var client = report.Kind == ProjectReportKind.Client;
        var title = client ? "Project summary" : "Project report";
        var footer = client
            ? $"{head.CompanyName} · Prepared for {project.Client?.DisplayName ?? "the client"}"
            : $"{head.CompanyName} · Internal: contains cost and profit information";
        var (document, section) = PdfLayout.NewDocument($"{title} · {project.Title}", footer);
        PdfLayout.AddLetterhead(section, head, title, "Generated " + PdfLayout.Date(report.Today));
        PdfLayout.AddTitle(section, project.Title, $"{PdfLayout.Date(report.From)} to {PdfLayout.Date(report.To)} · Reference {project.Id}");

        var money = project.Money;
        var isInternal = project.BillingType == BillingType.Internal;
        var worked = new
        {
            Minutes = report.Work.Contributors.Sum(c => c.Minutes),
            Overtime = report.Work.Contributors.Sum(c => c.OvertimeMinutes),
            Entries = report.Work.Contributors.Sum(c => c.Entries),
        };
        bool Show(ProjectReportSection s) => report.Sections.Contains(s);
        string Day(DateTime utc) => PdfLayout.Date(DateOnly.FromDateTime(report.ToCompanyTime(utc)));

        if (Show(ProjectReportSection.Summary))
        {
            PdfLayout.AddHeading(section, "Executive summary");
            var end = report.CompletedOn ?? report.Today;
            var facts = new List<Fact>
            {
                new("Client", project.Client?.DisplayName ?? "Internal project"),
                new("Status", Status(project.Status)),
                new("Duration", Count(end.DayNumber - project.StartDate.DayNumber + 1, "day")),
                new("Team", Count(report.Work.Contributors.Count, "person", "people")),
                new("Hours logged", Hours(worked.Minutes)),
            };
            if (money is not null)
            {
                if (!isInternal)
                {
                    facts.Add(new("Project value", PdfLayout.Money(money.Revenue)));
                }

                if (!client)
                {
                    facts.Add(new("Total cost", PdfLayout.Money(money.Cost.Total)));
                    facts.Add(isInternal
                        ? new("Company cost", PdfLayout.Money(money.Cost.Total))
                        : new(report.CompletedOn is null ? "Profit so far" : "Final profit", PdfLayout.Money(money.Profit)));
                    if (!isInternal)
                    {
                        facts.Add(new("Margin", Percent(money.Margin)));
                    }
                }

                if (!isInternal)
                {
                    facts.Add(new("Invoiced", PdfLayout.Money(money.Billing.Invoiced)));
                    facts.Add(new("Paid", PdfLayout.Money(money.Billing.Received)));
                    facts.Add(new("Outstanding", PdfLayout.Money(money.Billing.Outstanding)));
                }
            }

            PdfLayout.AddFactGrid(section, facts);
            if (!client)
            {
                PdfLayout.AddNote(section, "Profitability and collections are two separate measures. Profit is what the work earned; "
                    + "outstanding is money still to arrive. Neither one moves the other.");
            }
        }

        if (Show(ProjectReportSection.Info))
        {
            PdfLayout.AddHeading(section, "Project information");
            PdfLayout.AddFactGrid(section,
            [
                new("Reference", project.Id.ToString(CultureInfo.InvariantCulture)),
                new("Owner", project.Owner.Name),
                new("Start date", PdfLayout.Date(project.StartDate)),
                report.CompletedOn is { } completed
                    ? new("Completion date", PdfLayout.Date(completed))
                    : new("Target date", project.TargetDate is { } target ? PdfLayout.Date(target) : "-"),
                new("Progress", project.ProgressPct + "%"),
                new("Work entries", worked.Entries.ToString("N0", CultureInfo.InvariantCulture)),
            ]);
            if (!string.IsNullOrWhiteSpace(project.Description))
            {
                PdfLayout.AddNote(section, project.Description);
            }
        }

        if (Show(ProjectReportSection.Timeline) && report.Timeline is { } timeline)
        {
            PdfLayout.AddHeading(section, "Timeline");
            var workedDays = timeline.Days.Where(d => d.Minutes > 0).Select(d => d.Day).ToList();
            PdfLayout.AddFactGrid(section,
            [
                new("First work entry", workedDays.Count > 0 ? PdfLayout.Date(workedDays.Min()) : "-"),
                new("Last work entry", workedDays.Count > 0 ? PdfLayout.Date(workedDays.Max()) : "-"),
                new("Active weeks", $"{report.Work.Weekly.Count(w => w.Minutes > 0)} of {report.Work.Weekly.Count}"),
                new("Quiet periods", timeline.Gaps.Count > 0 ? $"{timeline.Gaps.Count} recorded" : "none"),
            ]);
            if (timeline.Events.Count > 0)
            {
                var events = PdfLayout.AddTable(section, new TableColumn("Date", 3.6), new TableColumn("Event", 13.8));
                foreach (var e in timeline.Events)
                {
                    PdfLayout.AddRow(events, false, Day(e.OccurredAt), Event(e));
                }
            }
        }

        if (Show(ProjectReportSection.Work))
        {
            PdfLayout.AddHeading(section, "Work and labour");
            var labour = report.Work.Contributors.Sum(c => c.LabourCost?.Usd ?? 0);
            var table = PdfLayout.AddTable(
                section,
                new TableColumn("Employee", 5),
                new TableColumn("Entries", 1.7, AlignRight: true),
                new TableColumn("Overtime", 2, AlignRight: true),
                new TableColumn("Hours", 2, AlignRight: true),
                new TableColumn("Share", 1.6, AlignRight: true),
                new TableColumn("Labour cost", 3.1, AlignRight: true),
                new TableColumn("Of cost", 2, AlignRight: true));
            foreach (var c in report.Work.Contributors)
            {
                var cost = c.LabourCost?.Usd ?? 0;
                var row = PdfLayout.AddRow(table, false,
                    c.Name,
                    c.Entries.ToString(CultureInfo.InvariantCulture),
                    c.OvertimeMinutes > 0 ? Hours(c.OvertimeMinutes) : "-",
                    Hours(c.Minutes),
                    Share(c.Minutes, worked.Minutes),
                    PdfLayout.Money(cost),
                    Share(cost, labour));
                if (c.LabourCost?.Unconverted is { Count: > 0 } other)
                {
                    PdfLayout.AddCellNote(row, 5, "+ " + string.Join(" + ", other.Select(a => PdfLayout.Money(a.Amount, a.Currency))));
                }
            }

            PdfLayout.AddRow(table, true, "Total", worked.Entries.ToString(CultureInfo.InvariantCulture), Hours(worked.Overtime),
                Hours(worked.Minutes), "100%", PdfLayout.Money(labour), "100%");
            PdfLayout.AddNote(section, $"Labour is each of the {worked.Entries:N0} work entries at the hourly rate in force on its day, "
                + "with the paid break of each day worked."
                + (money is not null ? $" Effective cost {PdfLayout.Money(money.CostPerHour)} per hour over the whole project." : string.Empty));
        }

        if (Show(ProjectReportSection.Expenses) && report.Expenses is { } expenses)
        {
            PdfLayout.AddHeading(section, "Project expenses");
            if (expenses.Items.Count == 0)
            {
                PdfLayout.AddParagraph(section, "No project expenses were recorded.");
            }
            else
            {
                var table = PdfLayout.AddTable(
                    section, new TableColumn("Date", 3), new TableColumn("Expense", 7.4), new TableColumn("Category", 3.5), new TableColumn("Amount", 3.5, AlignRight: true));
                foreach (var x in expenses.Items.OrderBy(x => x.SpentOn))
                {
                    PdfLayout.AddRow(table, false, PdfLayout.Date(x.SpentOn), x.Label, x.Category, PdfLayout.Money(x.Amount, x.Currency));
                }

                foreach (var total in expenses.Totals)
                {
                    PdfLayout.AddRow(table, true, "Total", null, null, PdfLayout.Money(total.Total, total.Currency));
                }
            }
        }

        if (Show(ProjectReportSection.Financials) && money is not null)
        {
            PdfLayout.AddHeading(section, isInternal ? "Cost summary" : "Financial summary");
            var facts = new List<Fact>();
            if (!isInternal)
            {
                facts.Add(new("Project value", PdfLayout.Money(money.Revenue)));
            }

            facts.Add(new("Employee labour", PdfLayout.Money(money.Cost.Labour)));
            facts.Add(new("Project expenses", PdfLayout.Money(money.Cost.Expenses)));
            facts.Add(new("Total cost", PdfLayout.Money(money.Cost.Total)));
            if (!isInternal)
            {
                facts.Add(new(report.CompletedOn is null ? "Profit so far" : "Final profit", PdfLayout.Money(money.Profit)));
                facts.Add(new("Margin", Percent(money.Margin)));
            }

            facts.Add(new("Cost per hour", PdfLayout.Money(money.CostPerHour)));
            PdfLayout.AddFactGrid(section, facts);
            PdfLayout.AddNote(section, "Company expenses are excluded: subscriptions, rent and other running costs are charged to the "
                + "company's monthly result, never to a single project. Client payments do not appear here either: cash has no "
                + "effect on profit or margin.");
        }

        if (Show(ProjectReportSection.Billing) && money is not null)
        {
            PdfLayout.AddHeading(section, "Billing and collections");
            PdfLayout.AddFactGrid(section,
            [
                new("Project value", PdfLayout.Money(money.Revenue)),
                new("Invoiced", PdfLayout.Money(money.Billing.Invoiced)),
                new("Paid", PdfLayout.Money(money.Billing.Received)),
                new("Outstanding", PdfLayout.Money(money.Billing.Outstanding)),
            ]);
            var invoices = report.Invoices.Where(i => i.Status is not (InvoiceStatus.Void or InvoiceStatus.Draft)).ToList();
            if (invoices.Count == 0)
            {
                PdfLayout.AddNote(section, "No invoice has been issued yet.");
            }
            else
            {
                var table = PdfLayout.AddTable(
                    section,
                    new TableColumn("Invoice", 3.2),
                    new TableColumn("Issued", 2.6),
                    new TableColumn("Due", 2.6),
                    new TableColumn("Amount", 2.5, AlignRight: true),
                    new TableColumn("Paid", 2.3, AlignRight: true),
                    new TableColumn("Balance", 2.3, AlignRight: true),
                    new TableColumn("Status", 1.9));
                foreach (var i in invoices)
                {
                    PdfLayout.AddRow(table, false,
                        i.Number,
                        i.IssuedAt is { } issued ? Day(issued) : "-",
                        i.DueOn is { } due ? PdfLayout.Date(due) : "-",
                        PdfLayout.Money(i.Total, i.Currency),
                        PdfLayout.Money(i.Paid ?? 0, i.Currency),
                        PdfLayout.Money(i.Balance ?? i.Total, i.Currency),
                        i.IsOverdue == true ? "Overdue" : InvoiceState(i.Status));
                }
            }

            var payments = report.Payments.Where(p => p.VoidedAt is null).ToList();
            if (payments.Count > 0)
            {
                var table = PdfLayout.AddTable(
                    section,
                    new TableColumn("Received", 3),
                    new TableColumn("Against", 3.2),
                    new TableColumn("Method", 2.6),
                    new TableColumn("Reference", 5.1),
                    new TableColumn("Amount", 3.5, AlignRight: true));
                foreach (var p in payments.OrderByDescending(p => p.ReceivedOn))
                {
                    PdfLayout.AddRow(table, false,
                        PdfLayout.Date(p.ReceivedOn), p.InvoiceNumber ?? "On account", Method(p.Method), p.Reference ?? "-", PdfLayout.Money(p.AmountUsd));
                }
            }

            if (money.Billing.Outstanding > 0)
            {
                PdfLayout.AddNote(section, $"{PdfLayout.Money(money.Billing.Outstanding)} remains outstanding. A completed project can still be owed money.");
            }
        }

        if (Show(ProjectReportSection.Client) && report.Client is { } who)
        {
            PdfLayout.AddHeading(section, "Client");
            PdfLayout.AddFactGrid(section,
            [
                new("Client", who.DisplayName),
                new("Contact", who.Name),
                new("Client since", Day(who.CreatedAt)),
                new("Projects to date", who.ProjectCount.ToString(CultureInfo.InvariantCulture)),
            ]);
            if (who.Contacts.Count > 0)
            {
                PdfLayout.AddNote(section, string.Join(" · ", who.Contacts.Select(x => x.Value)));
            }
        }

        if (Show(ProjectReportSection.Log))
        {
            PdfLayout.AddHeading(section, $"Work log (most recent {report.Log.Count} of {report.LogTotal:N0})");
            var table = PdfLayout.AddTable(
                section,
                new TableColumn("Date", 2.6),
                new TableColumn("Time", 2.6),
                new TableColumn("Duration", 1.8, AlignRight: true),
                new TableColumn("Employee", 3.6),
                new TableColumn("Work", 6.8));
            foreach (var e in report.Log)
            {
                PdfLayout.AddRow(table, false,
                    Day(e.StartedAt),
                    $"{report.ToCompanyTime(e.StartedAt):HH:mm}-{report.ToCompanyTime(e.EndedAt):HH:mm}",
                    Hours(e.Minutes),
                    e.UserName,
                    e.Description);
            }
        }

        return PdfLayout.Render(document);
    }

    private static string Hours(int minutes) => $"{minutes / 60}h {minutes % 60:00}m";

    private static string Share(decimal part, decimal whole) => whole > 0 ? $"{Math.Round(part * 100 / whole)}%" : "-";

    private static string Percent(decimal? ratio) => ratio is { } r ? (r * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";

    private static string Count(int n, string one, string? many = null) => $"{n:N0} {(n == 1 ? one : many ?? one + "s")}";

    private static string Status(ProjectStatus status) => status switch
    {
        ProjectStatus.Hold => "On hold",
        _ => status.ToString(),
    };

    private static string InvoiceState(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Partial => "Part paid",
        _ => status.ToString(),
    };

    private static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Bank => "Bank transfer",
        _ => method.ToString(),
    };

    private static string Event(TimelineEventResponse e) => e.Kind switch
    {
        ActivityEventKinds.ProjectCreated => "Project created",
        ActivityEventKinds.ProjectHold => "Put on hold",
        ActivityEventKinds.ProjectResumed => "Resumed",
        ActivityEventKinds.ProjectCompleted => "Marked as completed",
        ActivityEventKinds.ProjectArchived => "Archived",
        ActivityEventKinds.ProjectReopened => "Reopened",
        ActivityEventKinds.ProgressSet => $"Progress set to {e.Amount:0}%",
        _ => e.Kind.Replace('_', ' '),
    };
}
