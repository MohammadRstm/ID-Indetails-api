using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// The one rule for letting a client portal file go: only when nothing points at it any more, neither an item's draft
/// or published copy nor the draft or published hero. Checked against the project's items and settings as they stand
/// in the change being saved, under the project's content lock.
/// </summary>
internal static class PortalReferences
{
    /// <summary>Marks deleted (at <paramref name="now"/>) the candidates nothing points at, and returns them.</summary>
    /// <param name="items">Every item of the project that stays, as changed.</param>
    public static List<PortalFile> Release(IEnumerable<PortalFile> candidates, IEnumerable<PortalItem> items, PortalSettings settings, DateTime now)
    {
        var used = new HashSet<long>();
        foreach (var item in items)
        {
            // the navigation, when set, is newer than the key until the change is saved
            used.Add(item.File is { } file ? file.Id : item.FileId);
            if (item.PublishedFileId is { } published)
            {
                used.Add(published);
            }
        }

        if (settings.HeroFileId is { } hero)
        {
            used.Add(hero);
        }

        if (settings.PublishedHeroFileId is { } publishedHero)
        {
            used.Add(publishedHero);
        }

        var released = candidates.DistinctBy(f => f.Id).Where(f => f.Id != 0 && f.DeletedAt is null && !used.Contains(f.Id)).ToList();
        foreach (var file in released)
        {
            file.DeletedAt = now;
        }

        return released;
    }
}
