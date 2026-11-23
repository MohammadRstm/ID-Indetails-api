using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>Closed months reopened for logging (see <c>Business/WorkEntries/ReopenRules</c>).</summary>
public interface IReopenedMonthService
{
    /// <summary>The months open for logging again today, oldest first.</summary>
    Task<IReadOnlyList<ReopenedMonthResponse>> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a closed month (<c>yyyy-MM</c>) until a day, or moves that day; Design employees are told.</summary>
    Task<ReopenedMonthResponse> ReopenAsync(string month, ReopenMonthRequest request, CancellationToken cancellationToken = default);

    /// <summary>Locks a reopened month again now.</summary>
    Task EndAsync(string month, CancellationToken cancellationToken = default);
}
