using ID_Indetails.api.Business.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>When work can still change today: the open month and the closed months reopened for logging.</summary>
public interface IOpenMonths
{
    Task<OpenMonths> CurrentAsync(CancellationToken cancellationToken = default);
}
