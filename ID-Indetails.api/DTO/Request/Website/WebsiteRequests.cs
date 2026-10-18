using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Website;

/// <summary>Who took a stock photo and where it came from; all three together, or none.</summary>
public sealed record WebsiteImageCreditRequest
{
    public string Photographer { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// A website project, new or changed: every field, as the form shows it. A picture is a file the caller uploaded
/// (<c>POST /api/files</c>), or the one it already has.
/// </summary>
public sealed record WebsiteProjectRequest
{
    public string Title { get; init; } = string.Empty;

    /// <summary>The page address; empty makes one from the title (or keeps the current one).</summary>
    public string? Slug { get; init; }

    public WebsiteProjectType Type { get; init; }

    /// <summary>Null follows the linked project's status; required when there is no link.</summary>
    public WebsiteProjectStatus? Status { get; init; }

    /// <summary>Null follows the linked project's year; required when there is no link.</summary>
    public int? Year { get; init; }

    public string? Client { get; init; }

    public string? City { get; init; }

    public string? Country { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public string? Description { get; init; }

    /// <summary>The system project it shows, if any.</summary>
    public long? ProjectId { get; init; }

    public long? ThumbnailFileId { get; init; }

    public long? HeroFileId { get; init; }

    public string? HeroAlt { get; init; }

    public WebsiteImageCreditRequest? HeroCredit { get; init; }

    /// <summary>Shown on the website. Publishing needs everything the website shows.</summary>
    public bool Published { get; init; }
}

/// <summary>A picture added to a project's gallery, from the caller's upload.</summary>
public sealed record WebsiteImageRequest
{
    public long FileId { get; init; }

    public string Alt { get; init; } = string.Empty;

    public WebsiteImageCreditRequest? Credit { get; init; }
}

/// <summary>A gallery picture's description and credit.</summary>
public sealed record WebsiteImageEditRequest
{
    public string Alt { get; init; } = string.Empty;

    public WebsiteImageCreditRequest? Credit { get; init; }
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

/// <summary>One of the company's offices.</summary>
public sealed record WebsiteLocationRequest
{
    public string City { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public decimal Latitude { get; init; }

    public decimal Longitude { get; init; }

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
