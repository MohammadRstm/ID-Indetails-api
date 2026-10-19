using ID_Indetails.api.Constants;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>
/// The overdue invoices of the domains that send reminders, each checked and reminded on its own
/// (<see cref="IInvoiceService.SendReminderAsync"/>). A reminder whose email fails is recorded nowhere, so the next
/// hourly run tries it again.
/// </summary>
public sealed class InvoiceReminderService(
    IServiceScopeFactory scopes,
    IBillingSettingsRepository billingSettings,
    IInvoiceRepository invoices,
    IClock clock,
    ILogger<InvoiceReminderService> logger) : IInvoiceReminderService
{
    private const int PageSize = 100;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // a reminder due today goes from 9:00; one due earlier (the API was down, say) whenever the run comes
        var today = clock.CompanyToday;
        var dueBy = clock.ToCompanyTime(clock.UtcNow).Hour >= InvoiceScheduleService.SendFromHour ? today : today.AddDays(-1);
        var domains = new List<string>();
        foreach (var domain in DomainCodes.Businesses)
        {
            if (await billingSettings.GetAsync(domain, cancellationToken) is { RemindersEnabled: true })
            {
                domains.Add(domain);
            }
        }

        if (domains.Count == 0)
        {
            return;
        }

        var overdue = new List<long>();
        for (var page = 1; ; page++)
        {
            var (items, total) = await invoices.SearchAsync(
                new InvoiceSearch(domains, null, null, null, true, null, today, page, PageSize), cancellationToken);
            overdue.AddRange(items.Select(r => r.Invoice.Id));
            if (page * PageSize >= total)
            {
                break;
            }
        }

        foreach (var invoiceId in overdue)
        {
            await RemindAsync(invoiceId, dueBy, cancellationToken);
        }
    }

    /// <summary>In a scope of its own, so one that fails leaves nothing half-saved for the next.</summary>
    private async Task RemindAsync(long invoiceId, DateOnly dueBy, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var documents = services.GetRequiredService<IDocumentService>();
            await services.GetRequiredService<IInvoiceService>().SendReminderAsync(invoiceId, dueBy, documents.EmailInvoiceReminderAsync, cancellationToken);
            await services.GetRequiredService<ILiveUpdates>().FlushAsync(cancellationToken);
            await services.GetRequiredService<IPushQueue>().FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The reminder of invoice {InvoiceId} could not be sent; it is tried again on the next run.", invoiceId);
        }
    }
}
