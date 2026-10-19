using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A project shown on the company's public website. It may be linked to one of the system's projects: while
/// <see cref="Status"/> or <see cref="Year"/> is null it follows that project's, so the website stays current; a value
/// typed in here overrides it. Unlinked projects carry both. Nothing here is internal: everything can be public.
/// </summary>
public class WebsiteProject
{
    public long Id { get; set; }

    /// <summary>The page address on the website, /projects/{slug}: lowercase letters, digits and dashes, unique.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public WebsiteProjectType Type { get; set; }

    /// <summary>Null follows the linked project's status.</summary>
    public WebsiteProjectStatus? Status { get; set; }

    /// <summary>The year it was completed, or is expected to be. Null follows the linked project's.</summary>
    public int? Year { get; set; }

    /// <summary>The client's name as the website shows it; optional.</summary>
    public string? Client { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    /// <summary>Where the globe puts the project: decimal degrees.</summary>
    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    /// <summary>The point was picked at random in <see cref="City"/>, as only the city and country were given.</summary>
    public bool PositionApproximate { get; set; }

    /// <summary>Plain text.</summary>
    public string? Description { get; set; }

    /// <summary>The portrait picture (4:5) in the moving strip and the grid.</summary>
    public long? ThumbnailFileId { get; set; }

    /// <summary>The wide picture at the top of the project's page.</summary>
    public long? HeroFileId { get; set; }

    public string? HeroAlt { get; set; }

    /// <summary>The system project it shows, if any. One website project per system project.</summary>
    public long? ProjectId { get; set; }

    /// <summary>Only published projects reach the website.</summary>
    public bool Published { get; set; }

    /// <summary>Its place in the website's moving strip (1 first), or null when it isn't there. At most 20.</summary>
    public int? ConveyorOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedById { get; set; }

    public ICollection<WebsiteProjectImage> Images { get; set; } = new List<WebsiteProjectImage>();
}
