using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;

namespace ID_Indetails.api.Services.Billing.Implementation;

public sealed class QuoteService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IQuoteRepository quotes,
    IUnitOfWork unitOfWork,
    IClock clock) : IQuoteService
{
    private const string QuoteNotFound = "Quote not found.";

    public async Task<QuoteResponse> CreateAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        var net = project.Revenue;
        Throw(QuoteRules.CheckQuotable(project.DomainCode, project.BillingType, net));

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var year = clock.CompanyToday.Year;
        var quote = new Quote
        {
            ProjectId = project.Id,
            ClientId = project.ClientId!.Value,
            Number = QuoteRules.Number(year, await quotes.NextSequenceAsync(year, cancellationToken)),
            NetUsd = net,
            TvaRate = project.TvaRate,
            TotalUsd = Tva.Gross(net, project.TvaRate),
            Scope = string.IsNullOrWhiteSpace(project.Description) ? null : project.Description.Trim(),
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
        };
        quotes.Add(quote);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, quote.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<QuoteResponse>> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        return (await quotes.ListAsync(projectId, cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<QuoteResponse> GetAsync(long quoteId, CancellationToken cancellationToken = default)
        => await DescribeAsync(await ActorAsync(cancellationToken), quoteId, cancellationToken);

    public async Task<QuoteResponse> RecordDeliveryAsync(long quoteId, RecordDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await DescribeAsync(actor, quoteId, cancellationToken);
        quotes.AddDelivery(new QuoteDelivery
        {
            QuoteId = quoteId,
            Channel = request.Channel,
            Recipient = string.IsNullOrWhiteSpace(request.Recipient) ? null : request.Recipient.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            SentAt = clock.UtcNow,
            SentById = actor.UserId,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(actor, quoteId, cancellationToken);
    }

    /// <summary>The quote, read as not found outside the caller's domains.</summary>
    private async Task<QuoteResponse> DescribeAsync(UserAccess actor, long quoteId, CancellationToken cancellationToken)
    {
        var row = await quotes.GetAsync(quoteId, cancellationToken);
        return row is not null && actor.CanSee(row.Domain) ? ToResponse(row) : throw new NotFoundException(QuoteNotFound);
    }

    private static QuoteResponse ToResponse(QuoteRow row)
    {
        var q = row.Quote;
        PersonRefResponse? Person(long? id) => id is { } known && row.People.TryGetValue(known, out var name) ? new PersonRefResponse(known, name) : null;
        return new QuoteResponse(
            q.Id, q.Number, row.Domain, q.ProjectId, row.ProjectTitle, row.ProjectLocation,
            new InvoiceClientResponse(q.ClientId, row.ClientName),
            q.NetUsd, q.TvaRate, q.TotalUsd, q.Scope, q.CreatedAt,
            Person(q.CreatedById) ?? new PersonRefResponse(q.CreatedById, "Someone"),
            q.Deliveries.OrderByDescending(d => d.SentAt)
                .Select(d => new QuoteDeliveryResponse(d.Id, d.Channel, d.Recipient, d.SentAt, Person(d.SentById), d.Note))
                .ToList());
    }

    private async Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.InvoiceSend);

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
