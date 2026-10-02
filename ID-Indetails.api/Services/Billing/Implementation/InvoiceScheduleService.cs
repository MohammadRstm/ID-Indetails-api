using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>
/// Sends scheduled invoices: each is issued and emailed together (<see cref="IInvoiceService.SendScheduledAsync"/>),
/// so one whose email fails stays a scheduled draft and is tried again on the next hourly run.
/// </summary>
public sealed class InvoiceScheduleService(
    IServiceScopeFactory scopes,
    IInvoiceService invoiceService,
    IInvoiceRepository invoices,
    IClock clock,
    ILogger<InvoiceScheduleService> logger) : IInvoiceScheduleService
{
    /// <summary>The hour of the company day from which that day's scheduled invoices go out.</summary>
    public const int SendFromHour = 9;

    public async Task<IReadOnlyList<InvoiceResponse>> CreatePlanAsync(
        long projectId,
        InvoicePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = await invoiceService.CreatePlanAsync(projectId, request, cancellationToken);
        var today = clock.CompanyToday;
        if (plan.All(i => i.SendOn != today))
        {
            return plan;
        }

        foreach (var invoice in plan.Where(i => i.SendOn == today))
        {
            await SendAsync(invoice.Id, cancellationToken);
        }

        var sent = new List<InvoiceResponse>();
        foreach (var invoice in plan)
        {
            sent.Add(await invoiceService.GetAsync(invoice.Id, cancellationToken));
        }

        return sent;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.CompanyToday;
        var dueBy = clock.ToCompanyTime(clock.UtcNow).Hour >= SendFromHour ? today : today.AddDays(-1);
        foreach (var invoiceId in await invoices.ScheduledDueAsync(dueBy, cancellationToken))
        {
            await SendAsync(invoiceId, cancellationToken);
        }
    }

    /// <summary>Each in a scope of its own, so one that fails leaves nothing half-saved for the next to save.</summary>
    private async Task SendAsync(long invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var documents = services.GetRequiredService<IDocumentService>();
            await services.GetRequiredService<IInvoiceService>().SendScheduledAsync(invoiceId, documents.EmailScheduledInvoiceAsync, cancellationToken);
            await services.GetRequiredService<ILiveUpdates>().FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduled invoice {InvoiceId} could not be sent; it is tried again on the next run.", invoiceId);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IInvoiceService>().ScheduledSendFailedAsync(invoiceId, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<ILiveUpdates>().FlushAsync(cancellationToken);
        }
    }
}
