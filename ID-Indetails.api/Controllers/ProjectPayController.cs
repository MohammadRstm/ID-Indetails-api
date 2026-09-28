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

/// <summary>What Design projects paid their people. Shows individuals' pay, so it needs <c>salary:view</c>.</summary>
[ApiController]
[Route("api/company/project-pay")]
[HasPermission(PermissionCodes.SalaryView)]
public sealed class ProjectPayController(IProjectPayService projectPay) : ControllerBase
{
    [HttpGet]
    public Task<ProjectPayResponse> Get([FromQuery] ProjectPayQuery query, CancellationToken cancellationToken)
        => projectPay.GetAsync(query, cancellationToken);

    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Export([FromQuery] ProjectPayQuery query, CancellationToken cancellationToken)
    {
        var (fileName, csv) = await projectPay.ExportAsync(query, cancellationToken);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", fileName);
    }
}
