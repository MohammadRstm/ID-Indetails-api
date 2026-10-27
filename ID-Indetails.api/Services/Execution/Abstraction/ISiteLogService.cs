using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>The daily logs site staff write on the sites they oversee, and their list of sites.</summary>
public interface ISiteLogService
{
    /// <summary>Only the site's own staff, while it is open.</summary>
    Task<SiteLogResponse> CreateAsync(long projectId, SaveSiteLogRequest request, CancellationToken cancellationToken = default);

    /// <summary>The writer, on the company day it was written.</summary>
    Task<SiteLogResponse> UpdateAsync(long id, SaveSiteLogRequest request, CancellationToken cancellationToken = default);

    Task<PagedResponse<SiteLogResponse>> MineAsync(SiteLogListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Everyone's logs, on sites the caller can see (site_log:view_all).</summary>
    Task<PagedResponse<SiteLogResponse>> ListAsync(SiteLogListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MySiteResponse>> MySitesAsync(CancellationToken cancellationToken = default);
}
