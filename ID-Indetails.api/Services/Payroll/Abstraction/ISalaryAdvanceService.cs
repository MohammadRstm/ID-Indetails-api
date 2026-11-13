using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>Salary advances: people asking for part of their pay early, and super admins giving and deciding them.</summary>
public interface ISalaryAdvanceService
{
    Task<MySalaryAdvancesResponse> MineAsync(CancellationToken cancellationToken = default);

    Task<MySalaryAdvanceResponse> AskAsync(AskSalaryAdvanceRequest request, CancellationToken cancellationToken = default);

    Task<MySalaryAdvanceResponse> WithdrawAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResponse<SalaryAdvanceResponse>> ListAsync(SalaryAdvanceListQuery query, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> ApproveAsync(long id, ApproveSalaryAdvanceRequest request, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> DeclineAsync(long id, SalaryAdvanceNoteRequest request, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> RecordAsync(RecordSalaryAdvanceRequest request, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> ChangeMonthsAsync(long id, SalaryAdvanceMonthsRequest request, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> CancelAsync(long id, SalaryAdvanceNoteRequest request, CancellationToken cancellationToken = default);

    Task<SalaryAdvanceResponse> SettleAsync(long id, SettleSalaryAdvanceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The advances of these people that Salaries shows: given ones open or touching <paramref name="month"/>, worked out
    /// up to that month, for the caller (who holds <c>salary:view</c> or <c>salary_advance:manage</c>).
    /// </summary>
    Task<IReadOnlyList<SalaryAdvanceResponse>> ForSalaryAsync(User person, DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Requests waiting, what is left per currency and the leavers still to settle, among the people the caller sees.</summary>
    Task<SalaryAdvanceSummaryResponse> SummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Someone is leaving: tells those who give advances when they still have one to pay back. Called inside the
    /// deactivation's transaction, after their employment ended.
    /// </summary>
    Task NoteLeavingAsync(User person, long actorId, CancellationToken cancellationToken = default);
}
