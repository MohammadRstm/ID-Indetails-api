using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>What Design projects paid their people in a week, month or quarter (see <c>Business/Payroll/ProjectPay</c>).</summary>
public interface IProjectPayService
{
    Task<ProjectPayResponse> GetAsync(ProjectPayQuery query, CancellationToken cancellationToken = default);

    Task<(string FileName, string Csv)> ExportAsync(ProjectPayQuery query, CancellationToken cancellationToken = default);
}
