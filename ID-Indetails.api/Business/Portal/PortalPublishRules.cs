using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>An item's draft and published copies and whether it is chosen for the client, as the publishing rules compare them.</summary>
public sealed record PortalItemCopies(
    long Id, long FileId, string Title, PortalSection Section, int SortOrder,
    long? PublishedFileId, string? PublishedTitle, PortalSection? PublishedSection, int? PublishedSortOrder, bool Removed, bool ForClient);

/// <summary>What publishing now would change for clients.</summary>
public sealed record PortalChanges(
    int Added, int Updated, int Removed, IReadOnlyList<PortalSection> Reordered, bool HeroChanged, bool SummaryChanged)
{
    public bool Any => Added > 0 || Updated > 0 || Removed > 0 || Reordered.Count > 0 || HeroChanged || SummaryChanged;
}

/// <summary>
/// <list type="bullet">
/// <item>Staff work on a draft; clients see the published copy, which only publishing changes, all at once.</item>
/// <item>Of a project's files, only the client-relevant ones concern the client portal: those chosen for the client,
/// and those clients still see (published). A file the team keeps to itself changes nothing for clients.</item>
/// <item>A chosen item is new until first published, and changed when its file, title or section differs from the
/// published copy. A published item deleted or no longer chosen is removed: clients keep it until the next publish. A
/// new order shows as the section being reordered.</item>
/// <item>Only a JPEG, PNG or WebP image can be a hero image: what every browser shows. There are up to 12, each image
/// once, in order; the hero changed when the draft list (images and order) differs from the published one.</item>
/// </list>
/// </summary>
public static class PortalPublishRules
{
    public const int MaxSummaryLength = 2000;

    public const int MaxHeroes = 12;

    private static readonly string[] HeroTypes = ["image/jpeg", "image/png", "image/webp"];

    private static readonly PortalSection[] Sections = [PortalSection.Presentation, PortalSection.Model, PortalSection.Drawing];

    /// <summary>Chosen for the client, or still published to them.</summary>
    public static bool IsClientRelevant(bool forClient, long? publishedFileId) => forClient || publishedFileId is not null;

    /// <summary>The item compared with what clients see; null for a file that isn't for the client and never reached them.</summary>
    public static PortalItemState? StateOf(PortalItemCopies item)
    {
        if (!IsClientRelevant(item.ForClient, item.PublishedFileId))
        {
            return null;
        }

        // only a published item is ever marked removed, so a chosen one never published is new
        if (item.PublishedFileId is null)
        {
            return PortalItemState.New;
        }

        if (!item.ForClient || item.Removed)
        {
            return PortalItemState.Removed;
        }

        return item.FileId != item.PublishedFileId || item.Title != item.PublishedTitle || item.Section != item.PublishedSection
            ? PortalItemState.Changed
            : PortalItemState.Unchanged;
    }

    /// <param name="heroFileIds">The draft hero images' files, in order.</param>
    /// <param name="publishedHeroFileIds">The published hero images' files, in order.</param>
    public static PortalChanges ChangesOf(
        IReadOnlyList<PortalItemCopies> items, IReadOnlyList<long> heroFileIds, IReadOnlyList<long> publishedHeroFileIds,
        string? summary, string? publishedSummary)
    {
        var states = items.Select(StateOf).OfType<PortalItemState>().ToList();
        return new PortalChanges(
            states.Count(s => s == PortalItemState.New),
            states.Count(s => s == PortalItemState.Changed),
            states.Count(s => s == PortalItemState.Removed),
            ReorderedSections(items),
            !heroFileIds.SequenceEqual(publishedHeroFileIds),
            (summary ?? string.Empty) != (publishedSummary ?? string.Empty));
    }

    /// <summary>Another hero image fits only while the draft has fewer than <see cref="MaxHeroes"/>.</summary>
    public static RuleDenial? CheckCanAddHero(int draftHeroes)
        => draftHeroes >= MaxHeroes
            ? RuleDenial.BusinessRule(ErrorCodes.PortalHeroLimit, $"There can be at most {MaxHeroes} hero images. Remove one first.")
            : null;

    /// <summary>
    /// Sections whose published items, still chosen and staying where they are, come in a different order in the draft.
    /// New, removed and moved items count on their own, not as a new order; the team's own files don't count at all.
    /// </summary>
    public static IReadOnlyList<PortalSection> ReorderedSections(IReadOnlyList<PortalItemCopies> items)
    {
        var reordered = new List<PortalSection>();
        foreach (var section in Sections)
        {
            var staying = items.Where(i => i.ForClient && !i.Removed && i.Section == section && i.PublishedSection == section).ToList();
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
