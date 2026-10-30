using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Billing.Implementation;

public sealed class QuoteRepository(IdIndetailsDbContext db) : IQuoteRepository
{
    public async Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO `quote_sequences` (`year`, `last_number`) VALUES ({year}, 1) ON DUPLICATE KEY UPDATE `last_number` = `last_number` + 1",
            cancellationToken);

        return await db.Database
            .SqlQuery<int>($"SELECT `last_number` AS `Value` FROM `quote_sequences` WHERE `year` = {year}")
            .SingleAsync(cancellationToken);
    }

    public async Task<QuoteRow?> GetAsync(long id, CancellationToken cancellationToken = default)
        => (await RowsAsync(db.Quotes.Where(q => q.Id == id), cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<QuoteRow>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => RowsAsync(db.Quotes.Where(q => q.ProjectId == projectId).OrderByDescending(q => q.CreatedAt).ThenByDescending(q => q.Id), cancellationToken);

    public void Add(Quote quote) => db.Quotes.Add(quote);

    public void AddDelivery(QuoteDelivery delivery) => db.QuoteDeliveries.Add(delivery);

    private async Task<IReadOnlyList<QuoteRow>> RowsAsync(IQueryable<Quote> quotes, CancellationToken cancellationToken)
    {
        var rows = await quotes
            .AsNoTracking()
            .Include(q => q.Deliveries)
            .Select(q => new
            {
                Quote = q,
                q.Project.DomainCode,
                q.Project.Title,
                q.Project.Location,
                ClientName = q.Client.Name,
                q.Client.Organisation,
            })
            .ToListAsync(cancellationToken);
        var ids = rows.SelectMany(r => r.Quote.Deliveries.Select(d => d.SentById).Append(r.Quote.CreatedById))
            .OfType<long>().Distinct().ToList();
        var people = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email), cancellationToken);
        return rows.Select(r => new QuoteRow(
            r.Quote, r.DomainCode, r.Title, r.Location, ClientRules.DisplayName(r.ClientName, r.Organisation), people)).ToList();
    }
}
