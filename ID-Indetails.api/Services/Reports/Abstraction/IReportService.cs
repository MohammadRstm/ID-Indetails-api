using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Reports;

namespace ID_Indetails.api.Services.Reports.Abstraction;

public interface IReportService
{
    Task<WorkReportResponse> WorkReportAsync(WorkReportQuery query, CancellationToken cancellationToken = default);

    Task<OverviewResponse> OverviewAsync(OverviewQuery query, CancellationToken cancellationToken = default);
}
