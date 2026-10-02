namespace ID_Indetails.api.Services.Billing.Abstraction;

/// <summary>Emails clients reminders of their overdue invoices, in the domains whose billing settings turn them on.</summary>
public interface IInvoiceReminderService
{
    /// <summary>The hourly check: every overdue invoice whose next reminder is due (today's from 9:00, company time).</summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
