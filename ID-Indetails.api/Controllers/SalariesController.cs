using System.Text;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>What people are owed for a month. Calculated on every request, never stored.</summary>
[ApiController]
[Route("api/company/salaries")]
[HasPermission(PermissionCodes.SalaryView)]
public sealed class SalariesController(ISalaryService salaries) : ControllerBase
{
    [HttpGet]
    public Task<SalariesResponse> List([FromQuery] SalaryListQuery query, CancellationToken cancellationToken)
        => salaries.ListAsync(query, cancellationToken);

    /// <summary>How one person's pay adds up: hours owed, overtime work, excuses and deductions.</summary>
    [HttpGet("{userId:long}")]
    public Task<SalaryDetailResponse> Get(long userId, [FromQuery] SalaryMonthQuery query, CancellationToken cancellationToken)
        => salaries.GetAsync(userId, query, cancellationToken);

    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Export([FromQuery] SalaryListQuery query, CancellationToken cancellationToken)
    {
        var (fileName, csv) = await salaries.ExportAsync(query, cancellationToken);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", fileName);
    }
}
