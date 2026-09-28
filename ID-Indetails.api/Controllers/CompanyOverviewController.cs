using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Both domains and the company overhead together.</summary>
[ApiController]
[Route("api/company/overview")]
[HasPermission(PermissionCodes.CompanyView)]
public sealed class CompanyOverviewController(ICompanyOverviewService overview) : ControllerBase
{
    public const int MaxMonths = 36;

    /// <summary>A period's figures per domain and for the company; this month to date by default.</summary>
    [HttpGet]
    public Task<CompanyOverviewResponse> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => overview.GetAsync(from, to, cancellationToken);

    /// <summary>Revenue, costs and overhead month by month: the "profit before and after company expenses" chart.</summary>
    [HttpGet("monthly")]
    public Task<IReadOnlyList<CompanyMonthResponse>> Monthly([FromQuery] int months, CancellationToken cancellationToken)
        => overview.MonthlyAsync(Math.Clamp(months == 0 ? 13 : months, 1, MaxMonths), cancellationToken);
}
