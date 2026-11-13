using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Salary advances as super admins see them: deciding requests, recording advances and settling what is left.</summary>
[ApiController]
[Route("api/company/salary-advances")]
public sealed class SalaryAdvancesController(ISalaryAdvanceService advances) : ControllerBase
{
    [HttpGet]
    [HasAnyPermission(PermissionCodes.SalaryAdvanceManage, PermissionCodes.SalaryView)]
    public Task<PagedResponse<SalaryAdvanceResponse>> List([FromQuery] SalaryAdvanceListQuery query, CancellationToken cancellationToken)
        => advances.ListAsync(query, cancellationToken);

    [HttpGet("{id:long}")]
    [HasAnyPermission(PermissionCodes.SalaryAdvanceManage, PermissionCodes.SalaryView)]
    public Task<SalaryAdvanceResponse> Get(long id, CancellationToken cancellationToken) => advances.GetAsync(id, cancellationToken);

    /// <summary>An advance a super admin handed over, without a request.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.SalaryAdvanceManage)]
    public async Task<ActionResult<SalaryAdvanceResponse>> Record(RecordSalaryAdvanceRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await advances.RecordAsync(request, cancellationToken));

    [HttpPost("{id:long}/approve")]
    [HasPermission(PermissionCodes.SalaryAdvanceManage)]
    public Task<SalaryAdvanceResponse> Approve(long id, ApproveSalaryAdvanceRequest request, CancellationToken cancellationToken)
        => advances.ApproveAsync(id, request, cancellationToken);

    [HttpPost("{id:long}/decline")]
    [HasPermission(PermissionCodes.SalaryAdvanceManage)]
    public Task<SalaryAdvanceResponse> Decline(long id, SalaryAdvanceNoteRequest request, CancellationToken cancellationToken)
        => advances.DeclineAsync(id, request, cancellationToken);

    [HttpPut("{id:long}/months")]
    [HasPermission(PermissionCodes.SalaryAdvanceManage)]
    public Task<SalaryAdvanceResponse> ChangeMonths(long id, SalaryAdvanceMonthsRequest request, CancellationToken cancellationToken)
        => advances.ChangeMonthsAsync(id, request, cancellationToken);

    [HttpPost("{id:long}/cancel")]
    [HasPermission(PermissionCodes.SalaryAdvanceManage)]
    public Task<SalaryAdvanceResponse> Cancel(long id, SalaryAdvanceNoteRequest request, CancellationToken cancellationToken)
        => advances.CancelAsync(id, request, cancellationToken);

    [HttpPost("{id:long}/settle")]
    [HasPermission(PermissionCodes.SalaryAdvanceManage)]
    public Task<SalaryAdvanceResponse> Settle(long id, SettleSalaryAdvanceRequest request, CancellationToken cancellationToken)
        => advances.SettleAsync(id, request, cancellationToken);
}
