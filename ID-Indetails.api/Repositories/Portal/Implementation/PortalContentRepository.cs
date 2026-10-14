using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class PortalContentRepository(IdIndetailsDbContext db) : IPortalContentRepository
{
    public Task<PortalContentProject?> GetProjectAsync(long projectId, CancellationToken cancellationToken = default)
        => db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new PortalContentProject(p.Id, p.DomainCode, p.Title, p.Status, p.BillingType, p.ClientId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalItemView>> ItemsAsync(long projectId, CancellationToken cancellationToken = default)
        => await Views(db.PortalItems.Where(i => i.ProjectId == projectId)).ToListAsync(cancellationToken);

    public async Task<PortalSettingsView> SettingsAsync(long projectId, CancellationToken cancellationToken = default)
        => await (from s in db.PortalSettings.AsNoTracking()
                  where s.ProjectId == projectId
                  join u in db.Users on s.PublishedById equals u.Id into publishers
                  from publisher in publishers.DefaultIfEmpty()
                  select new PortalSettingsView(
                      s.HeroFileId, s.PublishedHeroFileId, s.Summary, s.PublishedSummary, s.Revision, s.PublishedAt, s.PublishedById,
                      publisher == null ? null : (publisher.FirstName + " " + publisher.LastName).Trim()))
            .SingleOrDefaultAsync(cancellationToken) ?? PortalSettingsView.Empty;

    public async Task<PortalSettings> LockSettingsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        // created by whoever comes first; the others find it. An existing row is locked exclusively right away (INSERT
        // IGNORE would take a shared lock, and two callers upgrading theirs at once deadlock).
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO `portal_settings` (`project_id`, `revision`) VALUES ({projectId}, 0) ON DUPLICATE KEY UPDATE `project_id` = `project_id`",
            cancellationToken);
        return (await db.PortalSettings
            .FromSql($"SELECT * FROM `portal_settings` WHERE `project_id` = {projectId} FOR UPDATE")
            .ToListAsync(cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<PortalItem>> ItemsToChangeAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.PortalItems.Include(i => i.File).Where(i => i.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalFile>> FilesAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        return wanted.Count == 0 ? [] : await db.PortalFiles.Where(f => wanted.Contains(f.Id)).ToListAsync(cancellationToken);
    }

    public Task<PortalFileView?> FileViewAsync(long fileId, CancellationToken cancellationToken = default)
        => db.PortalFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Select(f => new PortalFileView(
                f.Id, f.StorageKey, f.OriginalName, f.ContentType, f.OriginalSizeBytes, f.StoredSizeBytes, f.CreatedAt, f.UploadedById, null))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PresentationRow>> PresentationAsync(long projectId, bool published, CancellationToken cancellationToken = default)
    {
        var items = db.PortalItems.AsNoTracking().Where(i => i.ProjectId == projectId);
        if (published)
        {
            var rows = await (from i in items
                              where i.PublishedFileId != null
                              join f in db.PortalFiles on i.PublishedFileId equals f.Id
                              orderby i.PublishedSortOrder, i.Id
                              select new { i.Id, i.PublishedSection, i.PublishedTitle, File = f })
                .ToListAsync(cancellationToken);
            return rows.Select(r => new PresentationRow(r.Id, r.PublishedSection!.Value, r.PublishedTitle!, View(r.File))).ToList();
        }

        var drafts = await (from i in items
                            where i.RemovedAt == null
                            join f in db.PortalFiles on i.FileId equals f.Id
                            orderby i.SortOrder, i.Id
                            select new { i.Id, i.Section, i.Title, File = f })
            .ToListAsync(cancellationToken);
        return drafts.Select(r => new PresentationRow(r.Id, r.Section, r.Title, View(r.File))).ToList();
    }

    public Task<PortalFileView?> PublishedFileAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
        => (from i in db.PortalItems.AsNoTracking()
            where i.Id == itemId && i.ProjectId == projectId && i.PublishedFileId != null
            join f in db.PortalFiles on i.PublishedFileId equals f.Id
            select new PortalFileView(
                f.Id, f.StorageKey, f.OriginalName, f.ContentType, f.OriginalSizeBytes, f.StoredSizeBytes, f.CreatedAt, f.UploadedById, null))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ClientCanOpenAsync(long accountId, long projectId, CancellationToken cancellationToken = default)
        => (from a in db.PortalProjectAccess
            where a.AccountId == accountId && a.ProjectId == projectId && a.RevokedAt == null
            join p in db.Projects on a.ProjectId equals p.Id
            where p.DomainCode == DomainCodes.Design
            select a.Id).AnyAsync(cancellationToken);

    public async Task<bool> IsReferencedAsync(long fileId, CancellationToken cancellationToken = default)
        => await db.PortalItems.AnyAsync(i => i.FileId == fileId || i.PublishedFileId == fileId, cancellationToken)
            || await db.PortalSettings.AnyAsync(s => s.HeroFileId == fileId || s.PublishedHeroFileId == fileId, cancellationToken);

    private static PortalFileView View(PortalFile f)
        => new(f.Id, f.StorageKey, f.OriginalName, f.ContentType, f.OriginalSizeBytes, f.StoredSizeBytes, f.CreatedAt, f.UploadedById, null);

    public Task<PortalItemView?> ItemViewAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
        => Views(db.PortalItems.Where(i => i.ProjectId == projectId && i.Id == itemId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<PortalUsage> UsageAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var live = db.PortalFiles.AsNoTracking().Where(f => f.ProjectId == projectId && f.DeletedAt == null);
        return new PortalUsage(
            await live.CountAsync(cancellationToken),
            await live.SumAsync(f => (long?)f.OriginalSizeBytes, cancellationToken) ?? 0,
            await live.SumAsync(f => (long?)f.StoredSizeBytes, cancellationToken) ?? 0);
    }

    public async Task<PortalItem?> GetItemForUpdateAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
    {
        var item = (await db.PortalItems
            .FromSql($"SELECT * FROM `portal_items` WHERE `id` = {itemId} AND `project_id` = {projectId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (item is not null)
        {
            await db.Entry(item).Reference(i => i.File).LoadAsync(cancellationToken);
        }

        return item;
    }

    public Task<bool> ItemExistsAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
        => db.PortalItems.AnyAsync(i => i.ProjectId == projectId && i.Id == itemId, cancellationToken);

    public Task<int?> LastSortOrderAsync(long projectId, PortalSection section, CancellationToken cancellationToken = default)
        => db.PortalItems.Where(i => i.ProjectId == projectId && i.Section == section).MaxAsync(i => (int?)i.SortOrder, cancellationToken);

    public Task<PortalUpload?> FindUploadAsync(long createdById, string clientToken, CancellationToken cancellationToken = default)
        => db.PortalUploads.SingleOrDefaultAsync(u => u.CreatedById == createdById && u.ClientToken == clientToken, cancellationToken);

    public Task<PortalUpload?> GetUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default)
        => db.PortalUploads.SingleOrDefaultAsync(u => u.ProjectId == projectId && u.Id == uploadId, cancellationToken);

    public async Task<PortalUpload?> GetUploadForUpdateAsync(long uploadId, CancellationToken cancellationToken = default)
        => (await db.PortalUploads
            .FromSql($"SELECT * FROM `portal_uploads` WHERE `id` = {uploadId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public Task<long?> ItemIdForKeyAsync(string storageKey, CancellationToken cancellationToken = default)
        => db.PortalItems.Where(i => i.File.StorageKey == storageKey).Select(i => (long?)i.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalUpload>> UploadsReplacingAsync(long itemId, CancellationToken cancellationToken = default)
        => await db.PortalUploads.Where(u => u.ReplaceItemId == itemId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalUpload>> AbandonedUploadsAsync(DateTime cutoff, int limit, CancellationToken cancellationToken = default)
        => await db.PortalUploads
            .Where(u => u.Status == PortalUploadStatus.Uploading && u.LastActivityAt < cutoff)
            .OrderBy(u => u.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalUpload>> EndedUploadsAsync(DateTime cutoff, int limit, CancellationToken cancellationToken = default)
        => await db.PortalUploads
            .Where(u => u.Status != PortalUploadStatus.Uploading && u.LastActivityAt < cutoff)
            .OrderBy(u => u.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalFile>> FilesToDeleteAsync(int limit, CancellationToken cancellationToken = default)
        => await db.PortalFiles
            .Where(f => f.DeletedAt != null)
            .OrderBy(f => f.DeletedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<bool> IsLiveFileKeyAsync(string storageKey, CancellationToken cancellationToken = default)
        => db.PortalFiles.AnyAsync(f => f.StorageKey == storageKey && f.DeletedAt == null, cancellationToken);

    public async Task<HashSet<string>> KnownKeysAsync(CancellationToken cancellationToken = default)
    {
        var files = await db.PortalFiles.AsNoTracking().Select(f => f.StorageKey).ToListAsync(cancellationToken);
        var uploads = await db.PortalUploads.AsNoTracking().Select(u => u.StorageKey).ToListAsync(cancellationToken);
        return [.. files, .. uploads];
    }

    public Task<Dictionary<string, long>> LiveFileSizesAsync(CancellationToken cancellationToken = default)
        => db.PortalFiles.AsNoTracking()
            .Where(f => f.DeletedAt == null)
            .ToDictionaryAsync(f => f.StorageKey, f => f.StoredSizeBytes, cancellationToken);

    public void Add(PortalUpload upload) => db.PortalUploads.Add(upload);

    public void Add(PortalFile file) => db.PortalFiles.Add(file);

    public void Add(PortalItem item) => db.PortalItems.Add(item);

    public void Remove(PortalUpload upload) => db.PortalUploads.Remove(upload);

    public void Remove(PortalFile file) => db.PortalFiles.Remove(file);

    public void Remove(PortalItem item) => db.PortalItems.Remove(item);

    private IQueryable<PortalItemView> Views(IQueryable<PortalItem> items)
        => from i in items.AsNoTracking()
           join f in db.PortalFiles on i.FileId equals f.Id
           join uploader in db.Users on f.UploadedById equals uploader.Id into uploaders
           from uploader in uploaders.DefaultIfEmpty()
           join editor in db.Users on i.UpdatedById equals editor.Id into editors
           from editor in editors.DefaultIfEmpty()
           orderby i.Section, i.SortOrder, i.Id
           select new PortalItemView(
               i.Id, i.Section, i.Title, i.SortOrder, i.CreatedAt, i.UpdatedAt, i.UpdatedById,
               editor == null ? null : (editor.FirstName + " " + editor.LastName).Trim(),
               new PortalFileView(
                   f.Id, f.StorageKey, f.OriginalName, f.ContentType, f.OriginalSizeBytes, f.StoredSizeBytes, f.CreatedAt, f.UploadedById,
                   uploader == null ? null : (uploader.FirstName + " " + uploader.LastName).Trim()),
               i.PublishedFileId, i.PublishedTitle, i.PublishedSection, i.PublishedSortOrder, i.RemovedAt);
}
