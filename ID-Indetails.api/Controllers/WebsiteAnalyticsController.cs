using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Website.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The Website area's analytics: the company website's visitors, from Google Analytics. Needs
/// <c>website_analytics:view</c>. See docs/website.md, "Analytics".
/// </summary>
[ApiController]
[Route("api/website/analytics")]
[HasPermission(PermissionCodes.WebsiteAnalyticsView)]
[EnableRateLimiting(RateLimitPolicies.WebsiteAnalytics)]
public sealed class WebsiteAnalyticsController(IWebsiteAnalyticsService analytics) : ControllerBase
{
    /// <summary>A period's visitors, sources, pages, audience and events; the 28 days ending yesterday by default.</summary>
    [HttpGet]
    public Task<WebsiteAnalyticsResponse> Report([FromQuery] WebsiteAnalyticsQuery query, CancellationToken cancellationToken)
        => analytics.ReportAsync(query, cancellationToken);

    /// <summary>Who is on the website in the last 30 minutes.</summary>
    [HttpGet("realtime")]
    public Task<WebsiteAnalyticsRealtimeResponse> Realtime(CancellationToken cancellationToken) => analytics.RealtimeAsync(cancellationToken);
}
