using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>An item's draft and published copies, as the publishing rules compare them.</summary>
public sealed record PortalItemCopies(
    long Id, long FileId, string Title, PortalSection Section, int SortOrder,
    long? PublishedFileId, string? PublishedTitle, PortalSection? PublishedSection, int? PublishedSortOrder, bool Removed);

/// <summary>What publishing now would change for clients.</summary>
public sealed record PortalChanges(
    int Added, int Updated, int Removed, IReadOnlyList<PortalSection> Reordered, bool HeroChanged, bool SummaryChanged)
{
    public bool Any => Added > 0 || Updated > 0 || Removed > 0 || Reordered.Count > 0 || HeroChanged || SummaryChanged;
}

/// <summary>
/// <list type="bullet">
/// <item>Staff work on a draft; clients see the published copy, which only publishing changes, all at once.</item>
/// <item>An item is new until first published, removed when deleted after that (clients keep it until the next
/// publish), and changed when its file, title or section differs from the published copy. A new order shows as the
/// section being reordered.</item>
/// <item>Only a JPEG, PNG or WebP image can be the hero: what every browser shows.</item>
/// </list>
/// </summary>
public static class PortalPublishRules
{
    public const int MaxSummaryLength = 2000;

    private static readonly string[] HeroTypes = ["image/jpeg", "image/png", "image/webp"];

    private static readonly PortalSection[] Sections = [PortalSection.Presentation, PortalSection.Model, PortalSection.Drawing];

    public static PortalItemState StateOf(PortalItemCopies item)
    {
        if (item.Removed)
        {
            return PortalItemState.Removed;
        }

        if (item.PublishedFileId is null)
        {
            return PortalItemState.New;
        }

        return item.FileId != item.PublishedFileId || item.Title != item.PublishedTitle || item.Section != item.PublishedSection
            ? PortalItemState.Changed
            : PortalItemState.Unchanged;
    }

    public static PortalChanges ChangesOf(
        IReadOnlyList<PortalItemCopies> items, long? heroFileId, long? publishedHeroFileId, string? summary, string? publishedSummary)
    {
        var states = items.Select(StateOf).ToList();
        return new PortalChanges(
            states.Count(s => s == PortalItemState.New),
            states.Count(s => s == PortalItemState.Changed),
            states.Count(s => s == PortalItemState.Removed),
            ReorderedSections(items),
            heroFileId != publishedHeroFileId,
            (summary ?? string.Empty) != (publishedSummary ?? string.Empty));
    }

    /// <summary>
    /// Sections whose published items, staying where they are, come in a different order in the draft. New, removed and
    /// moved items count on their own, not as a new order.
    /// </summary>
    public static IReadOnlyList<PortalSection> ReorderedSections(IReadOnlyList<PortalItemCopies> items)
    {
        var reordered = new List<PortalSection>();
        foreach (var section in Sections)
        {
            var staying = items.Where(i => !i.Removed && i.Section == section && i.PublishedSection == section).ToList();
            var published = staying.OrderBy(i => i.PublishedSortOrder).ThenBy(i => i.Id).Select(i => i.Id);
            var draft = staying.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Id);
            if (!published.SequenceEqual(draft))
            {
                reordered.Add(section);
            }
        }

        return reordered;
    }

    /// <summary>A new order is taken only when it lists every current item of the section exactly once, and nothing else.</summary>
    public static bool IsWholeOrder(IReadOnlyCollection<long> current, IReadOnlyList<long> requested)
        => requested.Count == current.Count && requested.Distinct().Count() == requested.Count && requested.All(current.Contains);

    /// <summary>The place of the n-th item (0-based) in a new order.</summary>
    public static int SortOrderAt(int index) => (index + 1) * PortalContentRules.SortStep;

    public static bool CanBeHero(string contentType) => HeroTypes.Contains(contentType);

    /// <summary>An introduction as kept: trimmed, and null when there's nothing in it.</summary>
    public static string? CleanSummary(string? summary)
        => string.IsNullOrWhiteSpace(summary) ? null : summary.Trim().ReplaceLineEndings("\n");
}
