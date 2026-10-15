using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class ProjectFileCommentRepository(IdIndetailsDbContext db) : IProjectFileCommentRepository
{
    public async Task<IReadOnlyList<ProjectFileCommentView>> ListAsync(long itemId, CancellationToken cancellationToken = default)
        => await Views(db.ProjectFileComments.Where(c => c.ItemId == itemId)).ToListAsync(cancellationToken);

    public Task<ProjectFileCommentView?> ViewAsync(long commentId, CancellationToken cancellationToken = default)
        => Views(db.ProjectFileComments.Where(c => c.Id == commentId)).SingleOrDefaultAsync(cancellationToken);

    public Task<ProjectFileComment?> GetAsync(long projectId, long commentId, CancellationToken cancellationToken = default)
        => db.ProjectFileComments.SingleOrDefaultAsync(c => c.ProjectId == projectId && c.Id == commentId, cancellationToken);

    public async Task<IReadOnlyList<long>> AuthorsAsync(long itemId, CancellationToken cancellationToken = default)
        => await db.ProjectFileComments.AsNoTracking()
            .Where(c => c.ItemId == itemId)
            .Select(c => c.AuthorId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, ProjectFileCommentCounts>> CountsAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.ProjectFileComments.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .GroupBy(c => c.ItemId)
            .Select(g => new { ItemId = g.Key, Total = g.Count(), Open = g.Count(c => c.FixedAt == null) })
            .ToDictionaryAsync(g => g.ItemId, g => new ProjectFileCommentCounts(g.Total, g.Open), cancellationToken);

    public void Add(ProjectFileComment comment) => db.ProjectFileComments.Add(comment);

    public void Remove(ProjectFileComment comment) => db.ProjectFileComments.Remove(comment);

    private IQueryable<ProjectFileCommentView> Views(IQueryable<ProjectFileComment> comments)
        => from c in comments.AsNoTracking()
           join author in db.Users on c.AuthorId equals author.Id
           join fixer in db.Users on c.FixedById equals fixer.Id into fixers
           from fixer in fixers.DefaultIfEmpty()
           orderby c.Id
           select new ProjectFileCommentView(
               c.Id, c.ItemId, c.AuthorId, (author.FirstName + " " + author.LastName).Trim(), c.Body, c.CreatedAt, c.EditedAt,
               c.FixedAt, c.FixedById, fixer == null ? null : (fixer.FirstName + " " + fixer.LastName).Trim());
}
