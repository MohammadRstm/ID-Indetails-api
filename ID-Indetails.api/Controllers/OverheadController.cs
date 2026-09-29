using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Company expenses: recurring monthly overhead that belongs to neither domain.</summary>
[ApiController]
[Route("api/company/overhead")]
public sealed class OverheadController(IOverheadService overhead) : ControllerBase
{
    public const int MaxMonths = 36;

    [HttpGet]
    [HasAnyPermission(PermissionCodes.CompanyView, PermissionCodes.OverheadManage)]
    public Task<OverheadResponse> Get(CancellationToken cancellationToken)
        => overhead.GetAsync(cancellationToken);

    /// <summary>Overhead per month for the last <paramref name="months"/> months (this one included), per currency.</summary>
    [HttpGet("monthly")]
    [HasAnyPermission(PermissionCodes.CompanyView, PermissionCodes.OverheadManage)]
    public Task<IReadOnlyList<OverheadMonthResponse>> Monthly([FromQuery] int months, CancellationToken cancellationToken)
        => overhead.MonthlyAsync(Math.Clamp(months == 0 ? 13 : months, 1, MaxMonths), cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.OverheadManage)]
    public async Task<ActionResult<OverheadLineResponse>> Create(CreateOverheadRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await overhead.CreateAsync(request, cancellationToken));

    /// <summary>Name and group. The amount is changed separately, from a month.</summary>
    [HttpPut("{id:long}")]
    [HasPermission(PermissionCodes.OverheadManage)]
    public Task<OverheadLineResponse> Update(long id, UpdateOverheadRequest request, CancellationToken cancellationToken)
        => overhead.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:long}/amounts")]
    [HasPermission(PermissionCodes.OverheadManage)]
    public Task<OverheadLineResponse> ChangeAmount(long id, ChangeOverheadAmountRequest request, CancellationToken cancellationToken)
        => overhead.ChangeAmountAsync(id, request, cancellationToken);

    /// <summary>
    /// Charges the line in another currency from a month. A line charged before then ends and a new one takes over
    /// (returned), so past months keep their currency.
    /// </summary>
    [HttpPost("{id:long}/currency")]
    [HasPermission(PermissionCodes.OverheadManage)]
    public Task<OverheadLineResponse> ChangeCurrency(long id, ChangeOverheadCurrencyRequest request, CancellationToken cancellationToken)
        => overhead.ChangeCurrencyAsync(id, request, cancellationToken);

    /// <summary>Stops charging after this month. A line that hasn't started yet is removed (204).</summary>
    [HttpPost("{id:long}/cancel")]
    [HasPermission(PermissionCodes.OverheadManage)]
    public async Task<ActionResult<OverheadLineResponse>> Cancel(long id, CancellationToken cancellationToken)
        => await overhead.CancelAsync(id, cancellationToken) is { } line ? line : NoContent();

    /// <summary>Undoes a cancellation until the end of the month it was cancelled in.</summary>
    [HttpPost("{id:long}/resume")]
    [HasPermission(PermissionCodes.OverheadManage)]
    public Task<OverheadLineResponse> Resume(long id, CancellationToken cancellationToken)
        => overhead.ResumeAsync(id, cancellationToken);
}
