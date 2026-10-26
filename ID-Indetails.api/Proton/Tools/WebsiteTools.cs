using System.ComponentModel;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Website.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record WebsiteProjectsInput
{
    [Description("Only published ones (true) or only drafts (false). Leave out for all.")]
    public bool? Published { get; init; }
}

/// <param name="Missing">What it still needs before it can be published.</param>
/// <param name="StripPosition">Its place in the moving strip on the home page, if it is in it.</param>
public sealed record WebsiteProjectRow(
    long Id, string Title, string Type, string? Status, int? Year, string? Client, string? Place, bool Published, int? StripPosition,
    int Pictures, IReadOnlyList<string> Missing, string? LinkedProject, string Updated);

public sealed record WebsiteProjectsList(int Count, int Published, int StripLimit, IReadOnlyList<WebsiteProjectRow> Projects);

/// <summary>The projects on the company's public website, as its Website area lists them: never the pictures themselves.</summary>
public sealed class GetWebsiteProjectsTool(IWebsiteProjectService projects, IClock clock) : ProtonTool<WebsiteProjectsInput>
{
    private const int Max = 50;

    public override string Name => "get_website_projects";

    public override string Description =>
        "The projects on the company's public website (the Website area, not Design or Execution projects): title, type, " +
        "status and year, client and place, published or draft and what a draft still needs, and the home page's moving strip.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<WebsiteController>(nameof(WebsiteController.Projects));

    public override string? OnlyInDomain => DomainCodes.Website;

    protected override async Task<ProtonToolOutput> RunAsync(WebsiteProjectsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var list = await projects.ListAsync(cancellationToken);
        var rows = list.Projects
            .Where(p => input.Published is null || p.Published == input.Published)
            .Select(p => new WebsiteProjectRow(
                p.Id, p.Title, p.Type, p.ShownStatus is { } status ? System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString()) : null, p.ShownYear, p.Client,
                string.Join(", ", new[] { p.City, p.Country }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } place ? place : null,
                p.Published, p.ConveyorOrder, p.Gallery.Count + p.ConceptImages.Count + (p.HeroFileId is null ? 0 : 1),
                p.MissingToPublish, p.LinkedProject?.Title, clock.Local(p.UpdatedAt ?? p.CreatedAt)))
            .ToList();
        var shown = rows.Take(Max).ToList();
        return new ProtonToolOutput(
            new WebsiteProjectsList(rows.Count, list.Projects.Count(p => p.Published), list.ConveyorLimit, shown),
            shown.Select(r => new ProtonRef(ProtonRefType.WebsiteProject, r.Id, r.Title, DomainCodes.Website)).ToList());
    }
}

public sealed record WebsiteContentInput;

/// <param name="UnderConstruction">Since when the website shows its under-construction page, when it does.</param>
public sealed record WebsiteContent(
    IReadOnlyList<WebsiteServiceResponse> Services, IReadOnlyList<WebsiteLocationRow> Locations, IReadOnlyList<string> SocialLinks,
    string? ContactEmail, bool HasFavicon, string? UnderConstruction);

public sealed record WebsiteLocationRow(long Id, string City, string Country, string Address, string Phone);

/// <summary>What the public website says about the company: its services, offices, contact and social links, and whether it is under construction.</summary>
public sealed class GetWebsiteContentTool(IWebsiteContentService content, IClock clock) : ProtonTool<WebsiteContentInput>
{
    public override string Name => "get_website_content";

    public override string Description =>
        "What the company's public website says: its services, its offices (city, address, phone), the contact email, " +
        "social links, and whether it is under construction.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<WebsiteController>(nameof(WebsiteController.Projects));

    public override string? OnlyInDomain => DomainCodes.Website;

    protected override async Task<ProtonToolOutput> RunAsync(WebsiteContentInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var shown = await content.GetAsync(cancellationToken);
        return new ProtonToolOutput(new WebsiteContent(
            shown.Services,
            shown.Locations.Select(l => new WebsiteLocationRow(l.Id, l.City, l.Country, l.Address, l.Phone)).ToList(),
            shown.SocialLinks.Select(l => l.Label).ToList(),
            shown.ContactEmail,
            shown.FaviconFileId is not null,
            clock.Local(shown.UnderConstructionSince)), []);
    }
}

public sealed record WebsiteAnalyticsInput
{
    [Description("now: who is on the website at this moment. Leave out for a period's report.")]
    public bool? Now { get; init; }

    [Description("Default last30_days. The data ends yesterday.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Compare with the period before (default true).")]
    public bool? Compare { get; init; }
}

/// <summary>
/// The public website's visitors from Google Analytics, as the Website area's analytics page shows them: a period's
/// report or who is on it now. The service keeps each answer a while for everyone, so asking again costs nothing.
/// </summary>
public sealed class GetWebsiteAnalyticsTool(IWebsiteAnalyticsService analytics) : ProtonTool<WebsiteAnalyticsInput>
{
    private const int Top = 10;

    public override string Name => "get_website_analytics";

    public override string Description =>
        "The company's public website's visitors (Google Analytics): for a period, visitors, sessions, views, engagement, " +
        "contact requests, top pages, sources, countries and devices, compared with the period before; or, with now, who " +
        "is on the website this minute. Rates are fractions (0.42 = 42%), durations seconds.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<WebsiteAnalyticsController>(nameof(WebsiteAnalyticsController.Report));

    public override string? OnlyInDomain => DomainCodes.Website;

    public override bool Heavy => true;

    protected override async Task<ProtonToolOutput> RunAsync(WebsiteAnalyticsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Now == true)
        {
            var now = await analytics.RealtimeAsync(cancellationToken);
            return new ProtonToolOutput(new
            {
                visitors_now = now.VisitorsNow,
                visitors_last_30_minutes = now.VisitorsPerMinute.Sum(),
                pages = now.Pages.Take(Top),
                countries = now.Countries.Take(Top),
            }, []);
        }

        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.Last30Days, maxDays: 366);
        var query = new WebsiteAnalyticsQuery { From = range.From, To = range.To, Compare = input.Compare ?? true };
        await context.ValidateAsync(query, cancellationToken);
        var report = await analytics.ReportAsync(query, cancellationToken);
        return new ProtonToolOutput(new
        {
            report.Period,
            report.HasData,
            // Google leaves out small numbers to keep visitors anonymous: figures may be a little low
            report.Thresholded,
            report.Totals,
            report.PreviousTotals,
            channels = report.Channels.Take(Top),
            sources = report.Sources.Take(Top),
            pages = report.Pages.Take(Top),
            countries = report.Countries.Take(Top),
            report.Devices,
            report.VisitorTypes,
            events = report.Events.Take(Top),
        }, [], new DayRange(report.Period.From, report.Period.To));
    }
}
