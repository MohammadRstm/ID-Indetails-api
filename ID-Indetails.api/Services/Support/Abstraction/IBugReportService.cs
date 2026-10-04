using ID_Indetails.api.DTO.Request.Support;

namespace ID_Indetails.api.Services.Support.Abstraction;

public interface IBugReportService
{
    /// <summary>Emails the report to the support address, with who sent it and from which browser.</summary>
    Task SendAsync(ReportBugRequest request, string? userAgent, CancellationToken cancellationToken = default);
}
