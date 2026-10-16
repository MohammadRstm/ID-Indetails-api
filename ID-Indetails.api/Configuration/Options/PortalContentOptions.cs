using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Client portal content: how long its signed links last, and when its cleanup steps in.</summary>
public sealed class PortalContentOptions
{
    public const string SectionName = "PortalContent";

    /// <summary>An upload with no request for this long is abandoned; the cleanup removes it and whatever reached the store.</summary>
    [Range(1, 24 * 6)]
    public int UploadInactivityHours { get; init; } = 24;

    /// <summary>How long each link for sending one part stays valid. The browser asks for more as it goes.</summary>
    [Range(5, 24 * 60)]
    public int PartLinkMinutes { get; init; } = 60;

    /// <summary>How long the inline links on the content list (images, videos) stay valid.</summary>
    [Range(5, 24 * 60)]
    public int PreviewLinkMinutes { get; init; } = 60;

    /// <summary>How long a link asked for to download or open one file stays valid.</summary>
    [Range(1, 60)]
    public int DownloadLinkMinutes { get; init; } = 5;

    /// <summary>Whether the jobs run: the hourly cleanup and the weekly check against the store. Integration tests switch them off.</summary>
    public bool JobsEnabled { get; init; } = true;

    /// <summary>
    /// Whether the weekly check deletes objects in the store that no row knows about (after <see cref="OrphanGraceDays"/>).
    /// Off by default: after the database is restored from a backup, newer files would look like orphans.
    /// </summary>
    public bool DeleteOrphans { get; init; }

    [Range(1, 365)]
    public int OrphanGraceDays { get; init; } = 7;
}
