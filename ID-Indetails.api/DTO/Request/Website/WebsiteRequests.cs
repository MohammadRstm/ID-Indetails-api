using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Website;

/// <summary>
/// A website project, new or changed: every field, as the form shows it. A picture is a file the caller uploaded
/// (<c>POST /api/files</c>), or the one it already has.
/// </summary>
public sealed record WebsiteProjectRequest
{
    public string Title { get; init; } = string.Empty;


    public WebsiteProjectType Type { get; init; }

    /// <summary>Null follows the linked project's status; required when there is no link.</summary>
    public WebsiteProjectStatus? Status { get; init; }

    /// <summary>Null follows the linked project's year; required when there is no link.</summary>
    public int? Year { get; init; }

    public string? Client { get; init; }

    /// <summary>One of the country's cities in the list (<c>GET /api/website/places/cities</c>), or none.</summary>
    public string? City { get; init; }

    /// <summary>One of the list's countries (<c>GET /api/website/places/countries</c>); found from the coordinates when left out.</summary>
    public string? Country { get; init; }

    /// <summary>
    /// The exact point. Left out, the project gets a point near its city, or near one of its country's towns, kept
    /// until the city or country changes; sending that point back unchanged keeps it that way.
    /// </summary>
    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public string? Description { get; init; }

    /// <summary>The system project it shows, if any.</summary>
    public long? ProjectId { get; init; }

    public long? ThumbnailFileId { get; init; }

    public long? HeroFileId { get; init; }

    public string? HeroAlt { get; init; }

    /// <summary>Shown on the website. Publishing needs everything the website shows.</summary>
    public bool Published { get; init; }
}

/// <summary>A picture added to a project's gallery, from the caller's upload.</summary>
public sealed record WebsiteImageRequest
{
    public long FileId { get; init; }

    public string Alt { get; init; } = string.Empty;
}

/// <summary>A gallery picture's description.</summary>
public sealed record WebsiteImageEditRequest
{
    public string Alt { get; init; } = string.Empty;
}

/// <summary>A list's new order: every one of its current items, once.</summary>
public sealed record WebsiteOrderRequest
{
    public IReadOnlyList<long> Ids { get; init; } = [];
}

/// <summary>The website's moving strip, in order: up to 20 published projects. Empty clears it.</summary>
public sealed record WebsiteConveyorRequest
{
    public IReadOnlyList<long> ProjectIds { get; init; } = [];
}

/// <summary>One of the services the company offers.</summary>
public sealed record WebsiteServiceRequest
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// One of the company's offices: its coordinates, or its city and country (it then gets a random point in that city).
/// A city or country left out is found from the coordinates.
/// </summary>
public sealed record WebsiteLocationRequest
{
    public string? City { get; init; }

    public string? Country { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public string Address { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;
}

/// <summary>A link in the website's footer.</summary>
public sealed record WebsiteSocialLinkRequest
{
    public string Label { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// A message from the website's contact form. <see cref="Website"/> is a field people never see: anything in it means
/// a robot filled the form.
/// </summary>
public sealed record WebsiteContactMessageRequest
{
    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? Phone { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Website { get; init; }
}

/// <summary>
/// The Website area's analytics: company-time-zone days, both inclusive (both or neither). Defaults to the 28 days
/// ending yesterday; compared with the period of the same length before it unless <see cref="Compare"/> is false.
/// </summary>
public sealed record WebsiteAnalyticsQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public bool Compare { get; init; } = true;
}
