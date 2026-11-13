using ID_Indetails.api.DTO.Request.Support;

namespace ID_Indetails.api.Services.Support.Abstraction;

public interface IBugReportService
{
    /// <summary>Emails a signed-in staff member's message to the developer, with who sent it and from which browser, and sends them a copy.</summary>
    Task SendAsync(ReportBugRequest request, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>The same, for a client signed in to the portal.</summary>
    Task SendFromClientAsync(ReportBugRequest request, string? userAgent, CancellationToken cancellationToken = default);
}
