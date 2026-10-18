using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Files.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Files.Implementation;

public sealed class FileRepository(IdIndetailsDbContext db) : IFileRepository
{
    public async Task<long> BytesUploadedSinceAsync(long userId, DateTime since, CancellationToken cancellationToken = default)
        => await db.Files.Where(f => f.UploadedById == userId && f.CreatedAt >= since).SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0;

    public Task<StoredFile?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.Files.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<StoredFile?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => (await db.Files.FromSql($"SELECT * FROM `files` WHERE `id` = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<FileUses> UsesAsync(long id, CancellationToken cancellationToken = default)
        => new(
            await db.Users.AnyAsync(u => u.PhotoFileId == id, cancellationToken),
            await db.CompanySettings.AnyAsync(s => s.LogoFileId == id, cancellationToken),
            await db.MessageAttachments.Where(a => a.FileId == id).Select(a => a.Message.ConversationId).Distinct().ToListAsync(cancellationToken),
            await db.WorkEntries.Where(e => e.AttachmentFileId == id).Select(e => e.Id).ToListAsync(cancellationToken),
            await db.WebsiteProjects.AnyAsync(p => p.ThumbnailFileId == id || p.HeroFileId == id, cancellationToken)
                || await db.WebsiteProjectImages.AnyAsync(i => i.FileId == id, cancellationToken)
                || await db.CompanySettings.AnyAsync(s => s.WebsiteFaviconFileId == id, cancellationToken));

    public async Task<IReadOnlyList<StoredFile>> UnusedBeforeAsync(DateTime cutoff, int limit, CancellationToken cancellationToken = default)
        => await db.Files
            .Where(f => f.CreatedAt < cutoff
                && !db.Users.Any(u => u.PhotoFileId == f.Id)
                && !db.CompanySettings.Any(s => s.LogoFileId == f.Id || s.WebsiteFaviconFileId == f.Id)
                && !db.WebsiteProjects.Any(p => p.ThumbnailFileId == f.Id || p.HeroFileId == f.Id)
                && !db.WebsiteProjectImages.Any(i => i.FileId == f.Id)
                && !db.MessageAttachments.Any(a => a.FileId == f.Id)
                && !db.WorkEntries.Any(e => e.AttachmentFileId == f.Id))
            .OrderBy(f => f.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(StoredFile file) => db.Files.Add(file);

    public void Remove(StoredFile file) => db.Files.Remove(file);
}
