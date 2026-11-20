using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>Personal working weeks. People outside the caller's domains read as 404.</summary>
[ApiController]
[Route("api")]
[HasPermission(PermissionCodes.ScheduleManage)]
public sealed class SchedulesController(IScheduleService schedules, IDocumentService documents) : ControllerBase
{
    /// <summary>Everyone's week (their own or the company's) with the hours it requires.</summary>
    [HttpGet("company/schedules")]
    public Task<SchedulesResponse> List([FromQuery] ScheduleListQuery query, CancellationToken cancellationToken)
        => schedules.ListAsync(query, cancellationToken);

    /// <summary>The same people and weeks as a landscape PDF to print or keep.</summary>
    [HttpGet("company/schedules/pdf")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Pdf([FromQuery] ScheduleListQuery query, CancellationToken cancellationToken)
    {
        var pdf = await documents.SchedulesPdfAsync(query, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>The same people and weeks, one row each, as CSV.</summary>
    [HttpGet("company/schedules/export")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Export([FromQuery] ScheduleListQuery query, CancellationToken cancellationToken)
    {
        var csv = await documents.SchedulesCsvAsync(query, cancellationToken);
        return File(csv.Content, "text/csv", csv.FileName);
    }

    /// <summary>A person's own week, or back to the company week, from today or a later date.</summary>
    [HttpPut("employees/{userId:long}/schedule")]
    public Task<PersonScheduleResponse> Set(long userId, SetScheduleRequest request, CancellationToken cancellationToken)
        => schedules.SetAsync(userId, request, cancellationToken);
}
