using ID_Indetails.api.DTO.Request.TimeOff;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.TimeOff;

namespace ID_Indetails.api.Services.TimeOff.Abstraction;

/// <summary>Asking for time off, and the super admins deciding it (see <c>Business/TimeOff/TimeOffRules</c>).</summary>
public interface ITimeOffService
{
    /// <summary>The caller's own requests, newest first.</summary>
    Task<IReadOnlyList<TimeOffResponse>> MineAsync(CancellationToken cancellationToken = default);

    Task<TimeOffResponse> AskAsync(AskTimeOffRequest request, CancellationToken cancellationToken = default);

    /// <summary>The caller takes back their own pending request.</summary>
    Task<TimeOffResponse> WithdrawAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResponse<TimeOffResponse>> ListAsync(TimeOffListQuery query, CancellationToken cancellationToken = default);

    Task<TimeOffResponse> ApproveAsync(long id, ApproveTimeOffRequest request, CancellationToken cancellationToken = default);

    Task<TimeOffResponse> DeclineAsync(long id, TimeOffNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes back approved time off from today: the days before stay time off.</summary>
    Task<TimeOffResponse> RejectAsync(long id, TimeOffNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>Who is off, and when, on any day of the query: for the Off flags.</summary>
    Task<IReadOnlyList<AwayResponse>> AwayAsync(AwayQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// The time off nobody's action moves on: requests still pending on their first day expire, super admins are reminded
/// the day before (from 09:00), and on the first day the person's department and the super admins are told.
/// </summary>
public interface ITimeOffSchedule
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
