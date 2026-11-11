using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Reports;

namespace ID_Indetails.api.Services.Reports.Abstraction;

/// <summary>
/// Design employees' utilization in a month: logged against expected hours, excused hours, overtime and project mix,
/// hours only. Needs report:view; a caller who can't see Design gets not found.
/// </summary>
public interface IUtilizationService
{
    Task<UtilizationResponse> MonthAsync(UtilizationQuery query, CancellationToken cancellationToken = default);
}
