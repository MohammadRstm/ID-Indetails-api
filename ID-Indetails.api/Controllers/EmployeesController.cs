using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.Services.Employees.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// People management. Only people in a domain the caller can see are reachable; super admins
/// only for callers who see both domains. Everyone else reads as 404.
/// </summary>
[ApiController]
[Route("api/employees")]
public sealed class EmployeesController(IEmployeeService employeeService) : ControllerBase
{
    /// <summary>People list with search, filters and paging. Deactivated people only when asked for.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.EmployeeView)]
    public Task<PagedResponse<EmployeeSummaryResponse>> List([FromQuery] EmployeeListQuery query, CancellationToken cancellationToken)
        => employeeService.ListAsync(query, cancellationToken);

    /// <summary>A profile. Includes the current pay for callers with financial or pay access.</summary>
    [HttpGet("{userId:long}")]
    [HasPermission(PermissionCodes.EmployeeView)]
    public Task<EmployeeResponse> Get(long userId, CancellationToken cancellationToken)
        => employeeService.GetAsync(userId, cancellationToken);

    /// <summary>Replaces name, phone, position and team.</summary>
    [HttpPut("{userId:long}")]
    [HasPermission(PermissionCodes.EmployeeManage)]
    public Task<EmployeeResponse> Update(long userId, UpdateEmployeeRequest request, CancellationToken cancellationToken)
        => employeeService.UpdateAsync(userId, request, cancellationToken);

    /// <summary>Blocks sign-in and ends every session. History is kept.</summary>
    [HttpPost("{userId:long}/deactivate")]
    [HasPermission(PermissionCodes.EmployeeManage)]
    public Task<EmployeeResponse> Deactivate(long userId, CancellationToken cancellationToken)
        => employeeService.DeactivateAsync(userId, cancellationToken);

    /// <summary>Active again, or invited again if they never signed up.</summary>
    [HttpPost("{userId:long}/reactivate")]
    [HasPermission(PermissionCodes.EmployeeManage)]
    public Task<EmployeeResponse> Reactivate(long userId, CancellationToken cancellationToken)
        => employeeService.ReactivateAsync(userId, cancellationToken);

    /// <summary>Changes the role and clears personal permission changes.</summary>
    [HttpPut("{userId:long}/role")]
    [HasPermission(PermissionCodes.EmployeeManage)]
    public Task<EmployeeResponse> ChangeRole(long userId, ChangeRoleRequest request, CancellationToken cancellationToken)
        => employeeService.ChangeRoleAsync(userId, request, cancellationToken);

    /// <summary>Moves someone to the other domain and clears personal permission changes.</summary>
    [HttpPut("{userId:long}/domain")]
    [HasPermission(PermissionCodes.EmployeeManage)]
    public Task<EmployeeResponse> Move(long userId, MoveEmployeeRequest request, CancellationToken cancellationToken)
        => employeeService.MoveAsync(userId, request, cancellationToken);

    /// <summary>Pay history, newest first.</summary>
    [HttpGet("{userId:long}/pay")]
    [HasPermission(PermissionCodes.EmployeePayManage)]
    public Task<IReadOnlyList<PayResponse>> GetPay(long userId, CancellationToken cancellationToken)
        => employeeService.GetPayHistoryAsync(userId, cancellationToken);

    /// <summary>A new pay change, from today or an earlier day after the latest change.</summary>
    [HttpPost("{userId:long}/pay")]
    [HasPermission(PermissionCodes.EmployeePayManage)]
    public async Task<ActionResult<PayResponse>> ChangePay(long userId, ChangePayRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await employeeService.ChangePayAsync(userId, request, cancellationToken));
}
