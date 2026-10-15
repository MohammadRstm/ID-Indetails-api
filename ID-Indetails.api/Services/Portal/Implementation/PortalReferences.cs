using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// The one rule for letting a client portal file go: only when nothing points at it any more, neither an item's draft
/// or published copy nor a draft or published hero image. Checked against the project's items and hero images as they
/// stand in the change being saved, under the project's content lock.
/// </summary>
internal static class PortalReferences
{
    /// <summary>Marks deleted (at <paramref name="now"/>) the candidates nothing points at, and returns them.</summary>
    /// <param name="items">Every item of the project that stays, as changed.</param>
    /// <param name="heroes">Every hero image row of the project that stays, draft and published, as changed.</param>
    public static List<PortalFile> Release(
        IEnumerable<PortalFile> candidates, IEnumerable<PortalItem> items, IEnumerable<PortalHeroImage> heroes, DateTime now)
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

        foreach (var hero in heroes)
        {
            used.Add(hero.File is { } file ? file.Id : hero.FileId);
        }

        var released = candidates.DistinctBy(f => f.Id).Where(f => f.Id != 0 && f.DeletedAt is null && !used.Contains(f.Id)).ToList();
        foreach (var file in released)
        {
            file.DeletedAt = now;
        }

        return released;
    }
}
