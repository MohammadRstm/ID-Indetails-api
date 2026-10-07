using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Reports;

namespace ID_Indetails.api.Services.Reports.Abstraction;

public interface IReportService
{
    Task<WorkReportResponse> WorkReportAsync(WorkReportQuery query, CancellationToken cancellationToken = default);

    /// <summary>The work report's totals by project, person and client, without its charts, quarters or money: for comparing periods.</summary>
    Task<WorkTotalsResponse> WorkTotalsAsync(WorkReportQuery query, CancellationToken cancellationToken = default);

    Task<OverviewResponse> OverviewAsync(OverviewQuery query, CancellationToken cancellationToken = default);
}
