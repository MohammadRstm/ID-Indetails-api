using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Website;

/// <summary>
/// The rules of the company website's content (docs/website.md): which system status the website shows, which year,
/// what a project needs before it is published, and the moving strip's limit.
/// </summary>
public static partial class WebsiteRules
{
    /// <summary>The website's moving strip ("conveyor") shows at most this many projects.</summary>
    public const int MaxConveyor = 20;

    public const int MaxTitleLength = 150;
    public const int MaxTypeLength = 60;
    public const int MaxSlugLength = 100;
    public const int MaxClientLength = 150;
    public const int MaxPlaceLength = 100;
    public const int MaxDescriptionLength = 4000;

    /// <summary>The project page's Concept row shows at most this many pictures.</summary>
    public const int MaxConceptImages = 5;
    public const int MaxAltLength = 200;
    public const int MaxUrlLength = 500;
    public const int MaxServiceTitleLength = 120;
    public const int MaxServiceDescriptionLength = 1000;
    public const int MaxAddressLength = 300;
    public const int MaxPhoneLength = 50;
    public const int MaxLabelLength = 50;

    /// <summary>Active and on-hold projects are still in progress; completed and archived ones are done.</summary>
    public static WebsiteProjectStatus StatusOf(ProjectStatus status)
        => status is ProjectStatus.Completed or ProjectStatus.Archived ? WebsiteProjectStatus.Completed : WebsiteProjectStatus.InProgress;

    /// <summary>The year the website shows for a system project: when it was completed, else its target, else its start.</summary>
    public static int YearOf(DateTime? completedAt, DateOnly? targetDate, DateOnly startDate)
        => completedAt?.Year ?? targetDate?.Year ?? startDate.Year;

    /// <summary>
    /// A project's page address from its name: lowercase ASCII letters and digits joined by single dashes, accents
    /// dropped. Empty when the name has no Latin letter or digit.
    /// </summary>
    public static string SlugOf(string title)
    {
        var plain = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(c));
            }
        }

        var slug = NotSlug().Replace(plain.ToString(), "-").Trim('-');
        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        return slug;
    }

    public static bool IsSlug(string value) => value.Length <= MaxSlugLength && Slug().IsMatch(value);

    /// <summary>
    /// What a project still lacks before the website can show it: its page, the globe and the strip need all of these.
    /// The city is optional: a project may be placed in a country alone.
    /// Empty when it can be published.
    /// </summary>
    public static IReadOnlyList<string> MissingToPublish(
        WebsiteProjectStatus? status, int? year, string? country, decimal? latitude, decimal? longitude,
        string? description, long? thumbnailFileId, long? heroFileId)
    {
        var missing = new List<string>();
        if (status is null) missing.Add("a status");
        if (year is null) missing.Add("a year");
        if (string.IsNullOrWhiteSpace(country)) missing.Add("a country");
        if (latitude is null || longitude is null) missing.Add("a position on the map");
        if (string.IsNullOrWhiteSpace(description)) missing.Add("a description");
        if (thumbnailFileId is null) missing.Add("a card picture");
        if (heroFileId is null) missing.Add("a page picture");
        return missing;
    }

    /// <summary>Whether a new order lists exactly the current items, each once.</summary>
    public static bool IsWholeOrder(IEnumerable<long> current, IReadOnlyList<long> order)
    {
        var have = current.ToHashSet();
        return order.Count == have.Count && order.Distinct().Count() == order.Count && order.All(have.Contains);
    }

    /// <summary>"in-progress" or "completed", as the public website reads it.</summary>
    public static string PublicStatus(WebsiteProjectStatus status) => status == WebsiteProjectStatus.Completed ? "completed" : "in-progress";

    /// <summary>A type as typed, tidied: trimmed, single spaces and a capital first letter ("beach  house" is "Beach house").</summary>
    public static string CleanType(string type)
    {
        var tidy = Spaces().Replace(type.Trim(), " ");
        return tidy.Length == 0 ? tidy : char.ToUpperInvariant(tidy[0]) + tidy[1..];
    }

    /// <summary>
    /// The key the public site filters by, made like a page address ("Restaurant & café" is "restaurant-cafe"). Two
    /// types with the same key are one type.
    /// </summary>
    public static string TypeKey(string type) => SlugOf(type);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
