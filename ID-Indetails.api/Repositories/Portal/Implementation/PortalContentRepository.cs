using ID_Indetails.api.Business.Portal;
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
            .Select(p => new PortalContentProject(p.Id, p.DomainCode, p.Status, p.BillingType, p.ClientId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalItemView>> ItemsAsync(long projectId, CancellationToken cancellationToken = default)
        => await Views(db.PortalItems.Where(i => i.ProjectId == projectId)).ToListAsync(cancellationToken);

    public Task<PortalItemView?> ItemViewAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
        => Views(db.PortalItems.Where(i => i.ProjectId == projectId && i.Id == itemId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<PortalUsage> UsageAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var totals = await db.PortalFiles.AsNoTracking()
            .Where(f => f.ProjectId == projectId && f.DeletedAt == null)
            .GroupBy(_ => 1)
            .Select(g => new { Files = g.Count(), Original = g.Sum(f => f.OriginalSizeBytes), Stored = g.Sum(f => f.StoredSizeBytes) })
            .SingleOrDefaultAsync(cancellationToken);
        return totals is null ? new PortalUsage(0, 0, 0) : new PortalUsage(totals.Files, totals.Original, totals.Stored);
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
                   uploader == null ? null : (uploader.FirstName + " " + uploader.LastName).Trim()));
}
