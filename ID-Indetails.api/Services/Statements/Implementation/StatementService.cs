using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.Statements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Statements;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Materials.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Statements.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Statements.Abstraction;

namespace ID_Indetails.api.Services.Statements.Implementation;

public sealed class StatementService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IMaterialRepository materials,
    IStatementRepository statements,
    IProjectFinanceService finance,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IStatementService
{
    private const string ProjectNotFound = "Project not found.";
    private const string StatementNotFound = "Statement not found.";

    public async Task<PagedResponse<StatementSummaryResponse>> ListAsync(
        long projectId,
        StatementListQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        EnsureSite(actor, await projects.GetAsync(projectId, cancellationToken));

        var (items, total) = await statements.ListAsync(projectId, query.Page, query.PageSize, cancellationToken);
        var names = await statements.NamesAsync(items.SelectMany(r => r.Statement.Deliveries).Select(d => d.SentById ?? 0).Distinct().ToList(), cancellationToken);

        return new PagedResponse<StatementSummaryResponse>(
            items.Select(r => new StatementSummaryResponse(
                r.Statement.Id,
                r.Statement.Number,
                r.Statement.CreatedAt,
                r.Statement.Mode,
                r.Count,
                r.Total,
                r.Statement.Deliveries.Count,
                r.Statement.Deliveries.OrderByDescending(d => d.SentAt).ThenByDescending(d => d.Id).Select(d => ToResponse(d, names)).FirstOrDefault()))
                .ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<StatementResponse> GetAsync(long statementId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(actor, statementId, cancellationToken);
    }

    public async Task<StatementResponse> PreviewAsync(long projectId, CreateStatementRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = EnsureSite(actor, await projects.GetAsync(projectId, cancellationToken));
        var (lines, position, since) = await BuildAsync(project, request, cancellationToken);
        var document = StatementRules.Summarise(request.Mode, lines);
        var year = clock.CompanyToday.Year;

        return ToResponse(document, position) with
        {
            Number = StatementRules.Number(year, await statements.PeekSequenceAsync(year, cancellationToken)),
            IsPreview = true,
            ProjectId = project.Id,
            ProjectTitle = project.Title,
            Client = ClientOf(project.Client!),
            Mode = request.Mode,
            IncludesEarlier = request.IncludeEarlier,
            Since = since,
            CreatedAt = clock.UtcNow,
            Date = clock.CompanyToday,
        };
    }

    public async Task<StatementResponse> CreateAsync(long projectId, CreateStatementRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Locked so no material changes, and no other statement states the same materials, meanwhile.
        var project = EnsureSite(actor, await projects.GetForUpdateAsync(projectId, cancellationToken));
        var (lines, position, since) = await BuildAsync(project, request, cancellationToken);

        var statement = new ClientStatement
        {
            Number = StatementRules.Number(clock.CompanyToday.Year, await statements.NextSequenceAsync(clock.CompanyToday.Year, cancellationToken)),
            ProjectId = project.Id,
            ClientId = project.ClientId!.Value,
            Mode = request.Mode,
            IncludesEarlier = request.IncludeEarlier,
            Since = since,
            ReceivedUsd = position.Received,
            AgreedUsd = position.Agreed,
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
            Lines = lines.Select(l => new ClientStatementLine
            {
                MaterialId = l.MaterialId,
                Position = l.Position,
                Description = l.Description,
                Quantity = l.Quantity,
                Unit = l.Unit,
                Price = l.Price,
                IsEarlier = l.IsEarlier,
                IsListed = l.IsListed,
            }).ToList(),
        };
        statements.Add(statement);

        // A material shown to the client keeps that price from now on.
        var shownNow = lines.Where(l => !l.IsEarlier).ToDictionary(l => l.MaterialId, l => l.Price);
        foreach (var material in await materials.GetManyForUpdateAsync(shownNow.Keys, cancellationToken))
        {
            material.ClientPrice = shownNow[material.Id];
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        activity.Add(StatementEvent(statement, project.DomainCode, ActivityEventKinds.StatementCreated, actor.UserId,
            lines.Sum(l => l.Price), new { number = statement.Number, mode = request.Mode.ToString().ToLowerInvariant() }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, statement.Id, cancellationToken);
    }

    public async Task<StatementResponse> RecordDeliveryAsync(long statementId, RecordDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var statement = await statements.GetAsync(statementId, cancellationToken);
        if (statement is null || !actor.CanSee(statement.Project.DomainCode))
        {
            throw new NotFoundException(StatementNotFound);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        statements.AddDelivery(new ClientStatementDelivery
        {
            StatementId = statement.Id,
            Channel = request.Channel,
            Recipient = string.IsNullOrWhiteSpace(request.Recipient) ? null : request.Recipient.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            SentAt = clock.UtcNow,
            SentById = actor.UserId,
        });
        activity.Add(StatementEvent(statement, statement.Project.DomainCode, ActivityEventKinds.StatementSent, actor.UserId,
            statement.Lines.Sum(l => l.Price), new { number = statement.Number, channel = request.Channel.ToString().ToLowerInvariant() }));
        // Whoever sees the domain's money; the payload never holds amounts.
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.StatementSent, actor.UserId, statement.Project.DomainCode, statement.ProjectId, "statement",
                statement.Id, new { number = statement.Number }),
            await notifications.HoldersAsync(statement.Project.DomainCode, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, statement.Id, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private static Project EnsureSite(UserAccess actor, Project? project)
    {
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        return project.DomainCode == DomainCodes.Execution && project.ClientId is not null
            ? project
            : throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution sites have client statements.");
    }

    /// <summary>The lines a statement would have now, with where payment stands and the day of the previous statement.</summary>
    private async Task<(IReadOnlyList<StatementRules.Line> Lines, StatementRules.PaymentPosition Position, DateOnly? Since)> BuildAsync(
        Project project,
        CreateStatementRequest request,
        CancellationToken cancellationToken)
    {
        var markup = project.MaterialMarkup ?? 0;
        var candidates = (await materials.ListAsync(project.Id, cancellationToken))
            .Select(r => new StatementRules.Material(
                r.Material.Id,
                r.Material.Name,
                r.Material.Quantity,
                r.Material.Unit,
                ExecutionPricing.ClientPrice(new ExecutionPricing.Material(r.Material.Total, r.Material.ClientPrice), markup),
                r.IsStated,
                r.Material.AddedAt))
            .ToList();

        var (lines, denial) = StatementRules.Build(candidates, new StatementRules.Choice(
            request.Mode,
            request.IncludeEarlier,
            request.ExcludedMaterialIds.ToHashSet(),
            request.UnlistedTypeKeys.ToHashSet()));
        if (denial is not null)
        {
            throw denial.ToException();
        }

        var money = await finance.GetAsync(project, cancellationToken);
        var latest = await statements.LatestCreatedAtAsync(project.Id, cancellationToken);
        return (lines, new StatementRules.PaymentPosition(money.Received, money.Summary.Revenue), latest is { } at ? clock.ToCompanyDate(at) : null);
    }

    private async Task<StatementResponse> DescribeAsync(UserAccess actor, long statementId, CancellationToken cancellationToken)
    {
        var s = await statements.GetAsync(statementId, cancellationToken);
        if (s is null || !actor.CanSee(s.Project.DomainCode))
        {
            throw new NotFoundException(StatementNotFound);
        }

        var lines = s.Lines
            .OrderBy(l => l.Position)
            .Select(l => new StatementRules.Line(l.MaterialId, l.Position, l.Description, l.Quantity, l.Unit, l.Price, l.IsEarlier, l.IsListed))
            .ToList();
        var names = await statements.NamesAsync(
            s.Deliveries.Select(d => d.SentById ?? 0).Append(s.CreatedById ?? 0).Distinct().ToList(), cancellationToken);

        return ToResponse(StatementRules.Summarise(s.Mode, lines), new StatementRules.PaymentPosition(s.ReceivedUsd, s.AgreedUsd)) with
        {
            Id = s.Id,
            Number = s.Number,
            ProjectId = s.ProjectId,
            ProjectTitle = s.Project.Title,
            Client = ClientOf(s.Client),
            Mode = s.Mode,
            IncludesEarlier = s.IncludesEarlier,
            Since = s.Since,
            CreatedAt = s.CreatedAt,
            Date = clock.ToCompanyDate(s.CreatedAt),
            CreatedBy = Person(s.CreatedById, names),
            Deliveries = s.Deliveries.OrderBy(d => d.SentAt).ThenBy(d => d.Id).Select(d => ToResponse(d, names)).ToList(),
        };
    }

    private static StatementResponse ToResponse(StatementRules.Document document, StatementRules.PaymentPosition position) => new()
    {
        Types = document.Types.Select(t => new StatementTypeResponse(t.Key, t.Label, t.Quantity, t.Deliveries, t.Earlier, t.New, t.Total)).ToList(),
        HiddenTypes = document.HiddenTypes > 0 ? new StatementHiddenTypesResponse(document.HiddenTypes, document.HiddenTotal) : null,
        EarlierTotal = document.EarlierTotal,
        NewTotal = document.NewTotal,
        Total = document.Total,
        Count = document.Count,
        Received = position.Received,
        Agreed = position.Agreed,
        Remaining = position.Remaining,
        PaidAhead = position.PaidAhead,
    };

    private static StatementDeliveryResponse ToResponse(ClientStatementDelivery d, IReadOnlyDictionary<long, string> names)
        => new(d.Id, d.Channel, d.Recipient, d.SentAt, Person(d.SentById, names), d.Note);

    private static PersonRefResponse? Person(long? userId, IReadOnlyDictionary<long, string> names)
        => userId is { } id && names.TryGetValue(id, out var name) ? new PersonRefResponse(id, name) : null;

    private static ProjectClientResponse ClientOf(Client client) => new(client.Id, ClientRules.DisplayName(client.Name, client.Organisation));

    private ActivityEvent StatementEvent(ClientStatement statement, string domain, string kind, long actorId, decimal total, object payload) => new()
    {
        DomainCode = domain,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = statement.ProjectId,
        ClientId = statement.ClientId,
        SubjectType = "statement",
        SubjectId = statement.Id,
        Amount = total,
        Payload = JsonSerializer.Serialize(payload),
    };
}
