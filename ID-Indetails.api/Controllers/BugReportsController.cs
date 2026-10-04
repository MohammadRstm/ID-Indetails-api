using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Support.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>Bug reports from anyone signed in, emailed to the people who support the product.</summary>
[ApiController]
[Route("api/bug-reports")]
[Authorize]
public sealed class BugReportsController(IBugReportService bugReports) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.BugReport)]
    public async Task<IActionResult> Report(ReportBugRequest request, CancellationToken cancellationToken)
    {
        await bugReports.SendAsync(request, Request.Headers.UserAgent.ToString(), cancellationToken);
        return NoContent();
    }
}
