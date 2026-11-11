using System.Globalization;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>Reads through the invoice, quote and BOQ services, so visibility and what each caller sees stay theirs.</summary>
public sealed class DocumentService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IInvoiceService invoiceService,
    IBoqService boqService,
    IDesignVoService designVoService,
    IProjectService projectService,
    IProjectRepository projects,
    IMoneyRollupService moneyRollups,
    IWorkInsightsService workInsights,
    IExpenseService expenseService,
    IPaymentService paymentService,
    IClientService clientService,
    ICompanySettingsService companySettings,
    ISalaryService salaryService,
    IScheduleService scheduleService,
    IBillingSettingsRepository billingSettings,
    IEmailSender emailSender,
    IClock clock,
    ILogger<DocumentService> logger) : IDocumentService
{
    /// <summary>Invoices and payments listed in a report: the list endpoints' largest page.</summary>
    private const int MaxRows = 100;

    public async Task<PdfFile> InvoicePdfAsync(long invoiceId, CancellationToken cancellationToken = default)
        => await RenderInvoiceAsync(await invoiceService.GetAsync(invoiceId, cancellationToken), cancellationToken);

    public async Task<InvoiceResponse> EmailInvoiceAsync(long invoiceId, EmailDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await invoiceService.GetAsync(invoiceId, cancellationToken);
        InvoiceRules.CheckLive(invoice.IssuedAt is not null, invoice.VoidedAt is not null).ThrowIfDenied();

        var to = request.To.Trim();
        var (pdf, issuedOn, head, billing) = await InvoiceDocumentAsync(invoice, cancellationToken);
        await SendAsync(DocumentEmails.Invoice(to, invoice, issuedOn, head, billing, currentUser.FullName, pdf), "invoice", invoiceId, cancellationToken);

        return await invoiceService.RecordDeliveryAsync(
            invoiceId, new RecordDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = to, Note = request.Note }, cancellationToken);
    }

    public async Task EmailInvoiceReminderAsync(InvoiceResponse invoice, string to, CancellationToken cancellationToken = default)
    {
        var (pdf, issuedOn, head, billing) = await InvoiceDocumentAsync(invoice, cancellationToken);
        await SendAsync(DocumentEmails.InvoiceReminder(to, invoice, issuedOn, head, billing, pdf), "invoice reminder", invoice.Id, cancellationToken);
    }

    public async Task EmailScheduledInvoiceAsync(InvoiceResponse invoice, string to, CancellationToken cancellationToken = default)
    {
        var (pdf, issuedOn, head, billing) = await InvoiceDocumentAsync(invoice, cancellationToken);
        var sender = invoice.ScheduledBy?.Name ?? head.CompanyName;
        await SendAsync(DocumentEmails.Invoice(to, invoice, issuedOn, head, billing, sender, pdf), "invoice", invoice.Id, cancellationToken);
    }

    public async Task<PdfFile> BoqPdfAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var document = await boqService.GetDocumentAsync(boqId, cancellationToken);
        return BoqFile(document, await LetterheadAsync(cancellationToken));
    }

    public async Task<BoqDetailResponse> EmailBoqAsync(long boqId, EmailBoqRequest request, CancellationToken cancellationToken = default)
    {
        // checked before anything is sent, so an email that can't be recorded never goes out
        var document = await boqService.GetDocumentForSendingAsync(boqId, cancellationToken);
        var to = request.To.Trim();
        var head = await LetterheadAsync(cancellationToken);
        var date = BoqDate(document.Boq);
        var pdf = BoqFile(document, head);
        var kind = document.Boq.Kind == BoqKind.Vo ? "variation order" : "BOQ";
        await SendAsync(DocumentEmails.Boq(to, document, date, head, currentUser.FullName, pdf), kind, boqId, cancellationToken);

        return await boqService.RecordDeliveryAsync(
            boqId, new RecordBoqDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = to, Note = request.Note }, cancellationToken);
    }

    public async Task<PdfFile> DesignVoPdfAsync(long voId, CancellationToken cancellationToken = default)
    {
        var document = await designVoService.GetDocumentAsync(voId, cancellationToken);
        return DesignVoFile(document, await LetterheadAsync(cancellationToken));
    }

    public async Task EmailDesignVoAsync(long voId, EmailDocumentRequest request, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialWrite);
        var document = await designVoService.GetDocumentAsync(voId, cancellationToken);
        if (document.Vo.Status == DesignVoStatus.Cancelled)
        {
            throw new BusinessRuleException(ErrorCodes.DesignVoNotPending, "This variation order was cancelled, so it isn't sent.");
        }

        var head = await LetterheadAsync(cancellationToken);
        await SendAsync(
            DocumentEmails.DesignVo(request.To.Trim(), document, DesignVoDate(document.Vo), head, currentUser.FullName, DesignVoFile(document, head)),
            "variation order", voId, cancellationToken);
    }

    /// <summary>The day a VO was applied; a pending one carries the day it was sent.</summary>
    private DateOnly DesignVoDate(DesignVoResponse vo) => clock.ToCompanyDate(vo.AppliedAt ?? vo.SentAt ?? vo.CreatedAt);

    private PdfFile DesignVoFile(DesignVoDocument document, Letterhead head)
        => new(DesignVoPdf.Render(document, DesignVoDate(document.Vo), head), document.Vo.Number.ToLowerInvariant() + ".pdf");

    /// <summary>The day an agreed BOQ was agreed; a draft carries the day it is sent.</summary>
    private DateOnly BoqDate(BoqDetailResponse boq) => boq.AgreedAt is { } agreed ? clock.ToCompanyDate(agreed) : clock.CompanyToday;

    private PdfFile BoqFile(BoqDocument document, Letterhead head)
        => new(BoqPdf.Render(document, BoqDate(document.Boq), head), document.Boq.Number!.ToLowerInvariant() + ".pdf");

    public async Task<PdfFile> ProjectReportPdfAsync(long projectId, ProjectReportQuery query, CancellationToken cancellationToken = default)
    {
        // Design only (422 otherwise), and 404 outside the caller's domains.
        var work = await workInsights.ProjectWorkAsync(projectId, new ProjectWorkQuery { From = query.From, To = query.To }, cancellationToken);
        var project = await projectService.GetAsync(projectId, cancellationToken);
        var sections = ReportSections(query, project);
        bool Show(ProjectReportSection s) => sections.Contains(s);

        var today = clock.CompanyToday;
        var completedOn = project.CompletedAt is { } completedAt ? clock.ToCompanyDate(completedAt) : (DateOnly?)null;
        var from = query.From ?? project.StartDate;
        var to = query.To ?? completedOn ?? today;
        var log = Show(ProjectReportSection.Log)
            ? await workInsights.TimelineEntriesAsync(projectId, new TimelineEntriesQuery { From = from, To = to }, cancellationToken)
            : [];
        var billing = Show(ProjectReportSection.Billing);
        var seesClient = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Has(PermissionCodes.ClientView);
        var report = new ProjectReport(
            query.Kind,
            sections,
            from,
            to,
            today,
            completedOn,
            project,
            work,
            Show(ProjectReportSection.Timeline) ? await workInsights.TimelineAsync(projectId, cancellationToken) : null,
            Show(ProjectReportSection.Expenses) ? await expenseService.ListAsync(projectId, cancellationToken) : null,
            billing ? (await invoiceService.ListAsync(new InvoiceListQuery { ProjectId = projectId, PageSize = MaxRows }, cancellationToken)).Items : [],
            billing ? (await paymentService.ListAsync(new PaymentListQuery { ProjectId = projectId, PageSize = MaxRows }, cancellationToken)).Items : [],
            Show(ProjectReportSection.Client) && seesClient && project.Client is { } client ? await clientService.GetAsync(client.Id, cancellationToken) : null,
            log.OrderByDescending(e => e.StartedAt).Take(ProjectReportPdf.LogRows).ToList(),
            log.Count,
            clock.ToCompanyTime);

        var kind = query.Kind == ProjectReportKind.Client ? "client-summary" : "report";
        return new PdfFile(ProjectReportPdf.Render(report, await LetterheadAsync(cancellationToken)), $"project-{projectId}-{kind}-{today:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile> SalariesPdfAsync(SalaryListQuery query, CancellationToken cancellationToken = default)
    {
        // the whole filtered list through the Salaries page's own calculation (it also checks salary:view)
        var salaries = await salaryService.ListAsync(query with { Page = 1, PageSize = int.MaxValue }, cancellationToken);
        var domain = string.IsNullOrWhiteSpace(query.Domain) ? string.Empty : "-" + query.Domain;
        var name = $"salaries-{salaries.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture)}{domain}.pdf";
        var report = new SalariesReport(salaries, query.Domain, query.Role, query.Search, clock.CompanyToday);
        return new PdfFile(SalariesPdf.Render(report, await LetterheadAsync(cancellationToken)), name);
    }

    public async Task<PdfFile> SchedulesPdfAsync(ScheduleListQuery query, CancellationToken cancellationToken = default)
    {
        var report = await SchedulesReportAsync(query, cancellationToken);
        return new PdfFile(SchedulesPdf.Render(report, await LetterheadAsync(cancellationToken)), SchedulesFileName(report, "pdf"));
    }

    public async Task<CsvFile> SchedulesCsvAsync(ScheduleListQuery query, CancellationToken cancellationToken = default)
    {
        var report = await SchedulesReportAsync(query, cancellationToken);
        return new CsvFile(SchedulesCsv.Write(report), SchedulesFileName(report, "csv"));
    }

    /// <summary>The list the Schedules page shows, with the company's display settings.</summary>
    private async Task<SchedulesReport> SchedulesReportAsync(ScheduleListQuery query, CancellationToken cancellationToken)
    {
        var access = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ScheduleManage);
        var company = await companySettings.GetAsync(cancellationToken);
        var both = string.IsNullOrWhiteSpace(query.Domain) && access.VisibleDomains.Count(DomainCodes.IsBusiness) > 1;
        return new SchedulesReport(
            await scheduleService.ListAsync(query, cancellationToken), clock.CompanyToday, company.WeekStart, company.Use24HourTime, both, query.Domain, query.Search);
    }

    private static string SchedulesFileName(SchedulesReport report, string extension)
        => $"schedules-{report.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.{extension}";

    /// <summary>What was asked for (every section but the log by default), less what this kind and project can't show.</summary>
    private static HashSet<ProjectReportSection> ReportSections(ProjectReportQuery query, ProjectResponse project)
    {
        var sections = query.Sections.Count > 0
            ? query.Sections.ToHashSet()
            : Enum.GetValues<ProjectReportSection>().Where(s => s != ProjectReportSection.Log).ToHashSet();
        if (query.Kind == ProjectReportKind.Client)
        {
            sections.ExceptWith([ProjectReportSection.Work, ProjectReportSection.Expenses, ProjectReportSection.Financials]);
        }

        if (project.BillingType == BillingType.Internal)
        {
            sections.Remove(ProjectReportSection.Billing);
        }

        return sections;
    }

    private async Task<PdfFile> RenderInvoiceAsync(InvoiceResponse invoice, CancellationToken cancellationToken)
        => (await InvoiceDocumentAsync(invoice, cancellationToken)).Pdf;

    /// <summary>The invoice's PDF, and what its email shows beside it.</summary>
    private async Task<(PdfFile Pdf, DateOnly? IssuedOn, Letterhead Head, BillingSettings? Billing)> InvoiceDocumentAsync(
        InvoiceResponse invoice, CancellationToken cancellationToken)
    {
        var head = await LetterheadAsync(cancellationToken);
        var billing = await billingSettings.GetAsync(invoice.Domain, cancellationToken);
        var issuedOn = invoice.IssuedAt is { } at ? clock.ToCompanyDate(at) : (DateOnly?)null;
        var name = invoice.Number is { } number ? number.ToLowerInvariant() : $"invoice-draft-{invoice.Id}";
        // the whole project for its client, TVA included. Read straight from the project: whoever may send the invoice
        // may send its PDF, and the hourly run and reminders make it with nobody signed in.
        var project = await projects.GetAsync(invoice.Project.Id, cancellationToken);
        InvoiceProjectPosition? position = null;
        if (project is not null && invoice.VoidedAt is null)
        {
            var rollup = await moneyRollups.LoadAsync([project], cancellationToken);
            position = new InvoiceProjectPosition(
                rollup.Money(project.Id).RevenueWithTva,
                rollup.ProjectBillingOf(project).Received,
                invoice.Balance ?? invoice.Total);
        }
        return (new PdfFile(InvoicePdf.Render(invoice, issuedOn, head, billing, position), name + ".pdf"), issuedOn, head, billing);
    }

    /// <summary>The logo is left out when it is an image PDFs can't hold (only PNG and JPEG can).</summary>
    private async Task<Letterhead> LetterheadAsync(CancellationToken cancellationToken)
    {
        var company = await companySettings.GetAsync(cancellationToken);
        byte[]? logo = null;
        if (company.LogoFileId is not null)
        {
            var file = await companySettings.OpenLogoAsync(cancellationToken);
            await using var content = file.Content;
            if (file.MimeType is "image/png" or "image/jpeg")
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                logo = buffer.ToArray();
            }
        }

        return new Letterhead(company.Name, company.Address, company.Website, logo);
    }

    private async Task SendAsync(EmailMessage message, string kind, long id, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not email {Kind} {Id}.", kind, id);
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed, $"The {kind} could not be emailed, so nothing was recorded. Please try again.", ex);
        }
    }
}
