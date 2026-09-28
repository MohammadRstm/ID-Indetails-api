using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.WorkEntries.Implementation;

public sealed class WorkEntryCommentRepository(IdIndetailsDbContext db) : IWorkEntryCommentRepository
{
    public async Task<IReadOnlyList<WorkEntryCommentView>> ListAsync(long workEntryId, CancellationToken cancellationToken = default)
    {
        var rows = await db.WorkEntryComments
            .AsNoTracking()
            .Where(c => c.WorkEntryId == workEntryId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.AuthorId, c.Author.FirstName, c.Author.LastName, c.Author.Email, c.Author.RoleCode, c.Body, c.SentToGroup, c.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(c => new WorkEntryCommentView(
                c.Id, c.AuthorId, PersonName.Of(c.FirstName, c.LastName, c.Email), c.RoleCode, c.Body, c.SentToGroup, c.CreatedAt))
            .ToList();
    }

    public void Add(WorkEntryComment comment) => db.WorkEntryComments.Add(comment);
}
