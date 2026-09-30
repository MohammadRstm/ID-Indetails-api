using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Statements.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>Reads through the invoice and statement services, so visibility and what each caller sees stay theirs.</summary>
public sealed class DocumentService(
    ICurrentUser currentUser,
    IInvoiceService invoiceService,
    IStatementService statementService,
    IProjectService projectService,
    IWorkInsightsService workInsights,
    IExpenseService expenseService,
    IPaymentService paymentService,
    IClientService clientService,
    ICompanySettingsService companySettings,
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
        var pdf = await RenderInvoiceAsync(invoice, cancellationToken);
        var company = await companySettings.GetAsync(cancellationToken);
        await SendAsync(DocumentEmails.Invoice(to, invoice, company.Name, currentUser.FullName, pdf), "invoice", invoiceId, cancellationToken);

        return await invoiceService.RecordDeliveryAsync(
            invoiceId, new RecordDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = to, Note = request.Note }, cancellationToken);
    }

    public async Task<PdfFile> StatementPdfAsync(long statementId, CancellationToken cancellationToken = default)
        => await RenderStatementAsync(await statementService.GetAsync(statementId, cancellationToken), cancellationToken);

    public async Task<StatementResponse> EmailStatementAsync(long statementId, EmailDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var statement = await statementService.GetAsync(statementId, cancellationToken);

        var to = request.To.Trim();
        var pdf = await RenderStatementAsync(statement, cancellationToken);
        var company = await companySettings.GetAsync(cancellationToken);
        await SendAsync(DocumentEmails.Statement(to, statement, company.Name, currentUser.FullName, pdf), "statement", statementId, cancellationToken);

        return await statementService.RecordDeliveryAsync(
            statementId, new RecordDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = to, Note = request.Note }, cancellationToken);
    }

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
            Show(ProjectReportSection.Client) && project.Client is { } client ? await clientService.GetAsync(client.Id, cancellationToken) : null,
            log.OrderByDescending(e => e.StartedAt).Take(ProjectReportPdf.LogRows).ToList(),
            log.Count,
            clock.ToCompanyTime);

        var kind = query.Kind == ProjectReportKind.Client ? "client-summary" : "report";
        return new PdfFile(ProjectReportPdf.Render(report, await LetterheadAsync(cancellationToken)), $"project-{projectId}-{kind}-{today:yyyy-MM-dd}.pdf");
    }

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
    {
        var head = await LetterheadAsync(cancellationToken);
        var billing = await billingSettings.GetAsync(invoice.Domain, cancellationToken);
        var issuedOn = invoice.IssuedAt is { } at ? clock.ToCompanyDate(at) : (DateOnly?)null;
        var name = invoice.Number is { } number ? number.ToLowerInvariant() : $"invoice-draft-{invoice.Id}";
        return new PdfFile(InvoicePdf.Render(invoice, issuedOn, head, billing), name + ".pdf");
    }

    private async Task<PdfFile> RenderStatementAsync(StatementResponse statement, CancellationToken cancellationToken)
        => new(StatementPdf.Render(statement, await LetterheadAsync(cancellationToken)), statement.Number.ToLowerInvariant() + ".pdf");

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
