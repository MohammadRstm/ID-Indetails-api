using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;

namespace ID_Indetails.api.Services.Billing.Abstraction;

/// <summary>Auto invoice: drafts that are issued and emailed on their own days.</summary>
public interface IInvoiceScheduleService
{
    /// <summary>The plan's drafts, scheduled; any dated today go out at once.</summary>
    Task<IReadOnlyList<InvoiceResponse>> CreatePlanAsync(long projectId, InvoicePlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>The hourly check: every scheduled draft whose day has come, today's from 9:00.</summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
