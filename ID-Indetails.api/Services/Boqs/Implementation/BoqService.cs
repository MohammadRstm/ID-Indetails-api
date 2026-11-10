using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Boqs.Implementation;

public sealed class BoqService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IBoqRepository boqs,
    IProfitFloor floor,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IBoqService
{
    private const string ProjectNotFound = "Project not found.";
    private const string BoqNotFound = "BOQ not found.";

    public async Task<BoqListResponse> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !Reaches(actor, project))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        var caller = CallerOf(actor, project.OwnerId);
        if (!caller.CanDraft && !caller.CanSeeMoney && !caller.CanManagePricing && !caller.IsSiteOwner)
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "BOQs need the permission to write drafts, see money or manage pricing.");
        }

        Throw(BoqRules.CheckSite(project.DomainCode, project.BillingType));
        var rows = await boqs.ListAsync(projectId, cancellationToken);
        var readable = rows.Where(r => BoqRules.CanRead(r.Boq.Kind, r.Boq.AuthorId, r.Boq.SubmittedAt is not null, caller)).ToList();
        var names = await boqs.NamesAsync(UserIds(readable.Select(r => r.Boq)), cancellationToken);
        List<BoqSummaryResponse> Of(BoqKind kind) => readable.Where(r => r.Boq.Kind == kind).Select(r => Summary(r.Boq, r.LineCount, names)).ToList();

        var agreed = rows.Where(r => r.Boq.Status == BoqStatus.Agreed).Select(r => r.Boq).ToList();
        return new BoqListResponse(
            Of(BoqKind.Internal),
            Of(BoqKind.Client),
            Of(BoqKind.Vo),
            caller.CanSeeMoney || caller.CanManagePricing
                ? PriceOf(project, agreed.Where(b => b.Kind == BoqKind.Client).Sum(b => b.TotalUsd), agreed.Where(b => b.Kind == BoqKind.Vo).Sum(b => b.TotalUsd),
                    agreed.Sum(b => b.PermitsUsd))
                : null);
    }

    public async Task<BoqDetailResponse> GetAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(await ReadableAsync(actor, boqId, cancellationToken), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> CreateAsync(long projectId, CreateBoqRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedSiteAsync(actor, projectId, cancellationToken);
        RequireCreate(request.Kind, CallerOf(actor, project.OwnerId));
        Throw(BoqRules.CheckCanCreate(request.Kind, (await boqs.AgreedAsync(project.Id, cancellationToken)).ClientAgreed));

        var source = request.FromBoqId is { } fromId ? await SourceAsync(actor, project.Id, fromId, cancellationToken) : null;
        var reason = Clean(request.Reason);
        var title = Clean(request.Title) ?? BoqRules.DefaultTitle(request.Kind);
        var boq = NewBoq(project, request.Kind, title, reason, actor.UserId);
        foreach (var line in (source?.Lines ?? new List<BoqLine>()).OrderBy(l => l.Position))
        {
            boq.Lines.Add(CopyOf(line, boq.Lines.Count + 1));
        }

        await FinishAsync(boq, project, actor.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boq.Id, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> SaveAsync(long boqId, SaveBoqRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var site = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        var boq = await LockedBoqAsync(actor, boqId, site, cancellationToken);
        Throw(BoqRules.CheckEditable(boq.Status));
        if (request.UpdatedAt is { } loaded && !BoqRules.SameSave(boq.UpdatedAt, loaded))
        {
            throw new ConflictException(ErrorCodes.BoqChanged, "Someone saved this BOQ after you opened it. Reload it to see their changes.");
        }

        var reason = Clean(request.Reason);
        var inputs = request.Lines.Select(l => new BoqRules.Line(l.Quantity, l.WasteRate, l.Rate, l.Nature)).ToList();
        Throw(BoqRules.CheckContent(boq.Kind, reason, request.AdjustmentType, request.AdjustmentValue, inputs));

        var existing = boq.Lines.ToDictionary(l => l.Id);
        if (request.Lines.Any(l => l.Id is { } id && !existing.ContainsKey(id)))
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "lines", "A line doesn't belong to this BOQ.");
        }

        // a line left out is removed from the BOQ, which deletes it
        var kept = request.Lines.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToHashSet();
        foreach (var gone in boq.Lines.Where(l => !kept.Contains(l.Id)).ToList())
        {
            boq.Lines.Remove(gone);
        }

        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i)))
        {
            var target = line.Id is { } id ? existing[id] : new BoqLine();
            target.Position = index + 1;
            target.Nature = line.Nature;
            target.Code = Clean(line.Code);
            target.Description = line.Description.Trim();
            target.Quantity = line.Quantity;
            target.WasteRate = line.WasteRate;
            target.Unit = Clean(line.Unit);
            target.Rate = line.Rate;
            if (line.Id is null)
            {
                boq.Lines.Add(target);
            }
        }

        boq.Title = request.Title.Trim();
        boq.Reason = reason;
        boq.AdjustmentType = request.AdjustmentType;
        boq.AdjustmentValue = request.AdjustmentValue;
        Recompute(boq);
        boq.UpdatedAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await TellAuthorOfEditAsync(boq, site, actor, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> CopyLinesAsync(long boqId, CopyBoqLinesRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        var boq = await LockedBoqAsync(actor, boqId, project, cancellationToken);
        Throw(BoqRules.CheckEditable(boq.Status));

        var source = await SourceAsync(actor, project.Id, request.FromBoqId, cancellationToken);
        var chosen = request.LineIds is { } ids ? ids.ToHashSet() : null;
        if (chosen is not null && chosen.Any(id => source.Lines.All(l => l.Id != id)))
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "lineIds", "A line doesn't belong to the BOQ copied from.");
        }

        foreach (var line in source.Lines.Where(l => chosen is null || chosen.Contains(l.Id)).OrderBy(l => l.Position))
        {
            boq.Lines.Add(CopyOf(line, boq.Lines.Count + 1));
        }

        Throw(BoqRules.CheckContent(boq.Kind, boq.Reason, boq.AdjustmentType, boq.AdjustmentValue, LinesOf(boq)));
        Recompute(boq);
        boq.UpdatedAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await TellAuthorOfEditAsync(boq, project, actor, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> DuplicateAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var source = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedSiteAsync(actor, source.ProjectId, cancellationToken);
        RequireCreate(source.Kind, CallerOf(actor, project.OwnerId));
        Throw(BoqRules.CheckCanCreate(source.Kind, (await boqs.AgreedAsync(project.Id, cancellationToken)).ClientAgreed));

        var boq = NewBoq(project, source.Kind, BoqRules.CopyTitle(source.Title), source.Reason, actor.UserId);
        boq.AdjustmentType = source.AdjustmentType;
        boq.AdjustmentValue = source.AdjustmentValue;
        foreach (var line in source.Lines.OrderBy(l => l.Position))
        {
            boq.Lines.Add(CopyOf(line, boq.Lines.Count + 1));
        }

        await FinishAsync(boq, project, actor.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boq.Id, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task DeleteAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var site = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        var boq = await boqs.GetForUpdateAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound);
        EnsureReadable(boq, actor, site.OwnerId);
        if (!BoqRules.CanDelete(boq.Kind, boq.AuthorId, boq.SubmittedAt is not null, CallerOf(actor, site.OwnerId)))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, boq.Kind == BoqKind.Internal
                ? "Only its author deletes an internal draft, and only after withdrawing it from the site."
                : "Deleting client BOQs and variation orders needs the permission to manage pricing.");
        }

        Throw(BoqRules.CheckEditable(boq.Status));

        // soft-deleted: a draft that was sent keeps its deliveries on record
        boq.DeletedAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<BoqDetailResponse> AgreeAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        var boq = await LockedBoqAsync(actor, boqId, project, cancellationToken);

        var agreed = await boqs.AgreedAsync(project.Id, cancellationToken);
        Throw(BoqRules.CheckAgreeable(boq.Kind, boq.Status, boq.Lines.Count, agreed.ClientAgreed));
        var work = boq.Kind == BoqKind.Client
            ? BoqRules.WorkPrice(boq.TotalUsd, agreed.VoTotals)
            : BoqRules.WorkPrice(agreed.ClientTotal, [.. agreed.VoTotals, boq.TotalUsd]);
        var permits = boq.Kind == BoqKind.Client
            ? BoqRules.WorkPrice(boq.PermitsUsd, [agreed.VoPermits])
            : BoqRules.WorkPrice(agreed.ClientPermits, [agreed.VoPermits, boq.PermitsUsd]);
        Throw(BoqRules.CheckWorkPrice(work, permits));
        var before = await FloorBeforeAsync(project, work, permits, cancellationToken);

        var now = clock.UtcNow;
        boq.Status = BoqStatus.Agreed;
        boq.AgreedAt = now;
        boq.AgreedById = actor.UserId;
        boq.UpdatedAt = now;
        SetPrice(project, work, permits, boq.Number!, actor.UserId);
        activity.Add(BoqEvent(boq, project, ActivityEventKinds.BoqAgreed, actor.UserId, new { kind = KindOf(boq), number = boq.Number }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (before is not null)
        {
            await floor.CheckAsync(before, BlockedChangeKind.BoqAgree, new BlockedRequest<object?>(project.Id, null, ItemId: boq.Id), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> WithdrawAgreementAsync(long boqId, WithdrawBoqAgreementRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        var boq = await LockedBoqAsync(actor, boqId, project, cancellationToken);

        var agreed = await boqs.AgreedAsync(project.Id, cancellationToken);
        Throw(BoqRules.CheckWithdrawable(boq.Kind, boq.Status, agreed.VoTotals.Count));
        var work = boq.Kind == BoqKind.Client
            ? BoqRules.WorkPrice(0, agreed.VoTotals)
            : BoqRules.WorkPrice(agreed.ClientTotal, WithoutOne(agreed.VoTotals, boq.TotalUsd));
        var permits = boq.Kind == BoqKind.Client
            ? BoqRules.WorkPrice(0, [agreed.VoPermits])
            : BoqRules.WorkPrice(agreed.ClientPermits, [agreed.VoPermits, -boq.PermitsUsd]);
        Throw(BoqRules.CheckWorkPrice(work, permits));
        var before = await FloorBeforeAsync(project, work, permits, cancellationToken);

        var reason = request.Reason.Trim();
        boq.Status = BoqStatus.Draft;
        boq.AgreedAt = null;
        boq.AgreedById = null;
        boq.UpdatedAt = clock.UtcNow;
        SetPrice(project, work, permits, reason, actor.UserId);
        activity.Add(BoqEvent(boq, project, ActivityEventKinds.BoqAgreementWithdrawn, actor.UserId, new { kind = KindOf(boq), number = boq.Number, reason }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (before is not null)
        {
            await floor.CheckAsync(before, BlockedChangeKind.BoqWithdraw, new BlockedRequest<WithdrawBoqAgreementRequest>(project.Id, request, ItemId: boq.Id), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> RecordDeliveryAsync(long boqId, RecordBoqDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        var boq = await LockedBoqAsync(actor, boqId, project, cancellationToken);
        Throw(BoqRules.CheckSendable(boq.Kind, boq.Lines.Count));

        boqs.AddDelivery(new BoqDelivery
        {
            BoqId = boq.Id,
            Channel = request.Channel,
            Recipient = Clean(request.Recipient),
            Note = Clean(request.Note),
            TotalUsd = boq.TotalUsd + boq.PermitsUsd,
            SentAt = clock.UtcNow,
            SentById = actor.UserId,
        });
        activity.Add(BoqEvent(boq, project, ActivityEventKinds.BoqSent, actor.UserId,
            new { kind = KindOf(boq), number = boq.Number, channel = SnakeCaseEnum.ToDb(request.Channel) }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<IReadOnlyList<BoqSiteResponse>> SitesAsync(CancellationToken cancellationToken = default)
    {
        (await ActorAsync(cancellationToken)).Require(PermissionCodes.BoqCreate);
        return (await boqs.AuthorSitesAsync(cancellationToken)).Select(p => new BoqSiteResponse(p.Id, p.Title, p.Location, p.Status)).ToList();
    }

    public async Task<BoqSiteResponse> SiteAsync(long projectId, CancellationToken cancellationToken = default)
    {
        (await ActorAsync(cancellationToken)).Require(PermissionCodes.BoqCreate);
        var project = await projects.GetAsync(projectId, cancellationToken);
        return project is not null && BoqRules.IsAuthorSite(project.DomainCode, project.BillingType, project.Status)
            ? new BoqSiteResponse(project.Id, project.Title, project.Location, project.Status)
            : throw new NotFoundException(ProjectNotFound);
    }

    public async Task<BoqDetailResponse> SubmitAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var (actor, site, boq) = await AuthorsDraftAsync(boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        Throw(BoqRules.CheckSubmittable(boq.Kind, boq.SubmittedAt is not null, boq.Lines.Count));

        boq.SubmittedAt = clock.UtcNow;
        boq.SubmittedLines = JsonSerializer.Serialize(SubmittedLinesOf(boq));
        activity.Add(BoqEvent(boq, site, ActivityEventKinds.BoqSubmitted, actor.UserId, new { title = boq.Title, lines = boq.Lines.Count }));
        // the site's pricing managers and owner: whoever works from submitted drafts
        var recipients = (await notifications.HoldersAsync(DomainCodes.Execution, PermissionCodes.ProjectPricingManage, cancellationToken)).Append(site.OwnerId);
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.BoqSubmitted, actor.UserId, DomainCodes.Execution, site.Id, "boq", boq.Id,
                new { authorName = currentUser.FullName, title = boq.Title }),
            recipients,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> WithdrawSubmissionAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var (actor, site, boq) = await AuthorsDraftAsync(boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        Throw(BoqRules.CheckSubmitted(boq.Kind, boq.SubmittedAt is not null));

        boq.SubmittedAt = null;
        boq.SubmittedLines = null;
        activity.Add(BoqEvent(boq, site, ActivityEventKinds.BoqSubmissionWithdrawn, actor.UserId, new { title = boq.Title }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDetailResponse> KeepAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var (actor, site, boq) = await AuthorsDraftAsync(boqId, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        Throw(BoqRules.CheckSubmitted(boq.Kind, boq.SubmittedAt is not null));

        // the edited lines become what later edits are compared against
        boq.SubmittedLines = JsonSerializer.Serialize(SubmittedLinesOf(boq));
        activity.Add(BoqEvent(boq, site, ActivityEventKinds.BoqChangesKept, actor.UserId, new { title = boq.Title }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(await boqs.GetAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound), actor, cancellationToken);
    }

    public async Task<BoqDocument> GetDocumentAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var boq = await ReadableAsync(actor, boqId, cancellationToken);
        Throw(BoqRules.CheckClientFacing(boq.Kind));
        return await DocumentAsync(boq, cancellationToken);
    }

    public async Task<BoqDocument> GetDocumentForSendingAsync(long boqId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var boq = await ReadableAsync(actor, boqId, cancellationToken);
        RequireWrite(boq, actor, boq.Project.OwnerId);
        Throw(SiteRules.CheckOpen(boq.Project.DomainCode, boq.Project.Status, boq.Project.RevenueLockedAt is not null));
        Throw(BoqRules.CheckSendable(boq.Kind, boq.Lines.Count));
        return await DocumentAsync(boq, cancellationToken);
    }

    public async Task AddClosingPriceAsync(Project project, decimal work, string title, string reason, long actorId, CancellationToken cancellationToken = default)
    {
        var agreed = await boqs.AgreedAsync(project.Id, cancellationToken);
        var kind = agreed.ClientAgreed ? BoqKind.Vo : BoqKind.Client;
        var change = work - (project.ProjectFee ?? 0);
        var boq = NewBoq(project, kind, title, kind == BoqKind.Vo ? reason : null, actorId);
        boq.Lines.Add(new BoqLine { Position = 1, Description = title, Quantity = 1, WasteRate = 0, Unit = "L.S", Rate = change });
        await FinishAsync(boq, project, actorId, cancellationToken);

        var now = clock.UtcNow;
        boq.Status = BoqStatus.Agreed;
        boq.AgreedAt = now;
        boq.AgreedById = actorId;
        SetPrice(project, work, project.PermitsPrice ?? 0, boq.Number!, actorId);
        activity.Add(BoqEvent(boq, project, ActivityEventKinds.BoqAgreed, actorId, new { kind = KindOf(boq), number = boq.Number }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private static BoqRules.Caller CallerOf(UserAccess actor, long siteOwnerId)
        => new(actor.UserId, actor.Has(PermissionCodes.BoqCreate), actor.Has(PermissionCodes.FinancialView), actor.Has(PermissionCodes.ProjectPricingManage),
            siteOwnerId == actor.UserId);

    /// <summary>The caller sees the site's domain, or writes BOQs and it is an active Execution client site.</summary>
    private static bool Reaches(UserAccess actor, Project project)
        => actor.CanSee(project.DomainCode)
            || (actor.Has(PermissionCodes.BoqCreate) && BoqRules.IsAuthorSite(project.DomainCode, project.BillingType, project.Status));

    /// <summary>
    /// The project locked for the rest of the transaction, so agreeing, saving and numbering one project's BOQs wait for
    /// each other. BOQs change only on an Execution client project the caller can see, and only while it is open.
    /// </summary>
    private async Task<Project> LockedSiteAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !Reaches(actor, project))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        Throw(BoqRules.CheckSite(project.DomainCode, project.BillingType));
        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        return project;
    }

    /// <summary>The BOQ as the caller may read it, not tracked, with its project, lines and deliveries.</summary>
    private async Task<Boq> ReadableAsync(UserAccess actor, long boqId, CancellationToken cancellationToken)
    {
        var boq = await boqs.GetAsync(boqId, cancellationToken);
        if (boq is null || !Reaches(actor, boq.Project))
        {
            throw new NotFoundException(BoqNotFound);
        }

        EnsureReadable(boq, actor, boq.Project.OwnerId);
        return boq;
    }

    /// <summary>The BOQ tracked, after the project is locked, for a change the caller may make.</summary>
    private async Task<Boq> LockedBoqAsync(UserAccess actor, long boqId, Project site, CancellationToken cancellationToken)
    {
        var boq = await boqs.GetForUpdateAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound);
        RequireWrite(boq, actor, site.OwnerId);
        return boq;
    }

    /// <summary>Another author's internal draft doesn't exist for the caller; a client BOQ they may not read is forbidden.</summary>
    private static void EnsureReadable(Boq boq, UserAccess actor, long siteOwnerId)
    {
        if (BoqRules.CanRead(boq.Kind, boq.AuthorId, boq.SubmittedAt is not null, CallerOf(actor, siteOwnerId)))
        {
            return;
        }

        throw boq.Kind == BoqKind.Internal
            ? new NotFoundException(BoqNotFound)
            : new ForbiddenException(ErrorCodes.Forbidden, "Client BOQs and variation orders need the permission to see money or manage pricing.");
    }

    private static void RequireWrite(Boq boq, UserAccess actor, long siteOwnerId)
    {
        EnsureReadable(boq, actor, siteOwnerId);
        if (!BoqRules.CanWrite(boq.Kind, boq.AuthorId, boq.SubmittedAt is not null, CallerOf(actor, siteOwnerId)))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, boq.Kind == BoqKind.Internal
                ? "A submitted draft is changed by the site's pricing managers and owner: withdraw it to change it yourself."
                : "Changing client BOQs and variation orders needs the permission to manage pricing.");
        }
    }

    private static void RequireCreate(BoqKind kind, BoqRules.Caller caller)
    {
        if (!BoqRules.CanCreate(kind, caller))
        {
            throw new ForbiddenException(
                ErrorCodes.Forbidden,
                kind == BoqKind.Internal ? "Writing internal drafts needs the permission to create BOQs." : "Making client BOQs and variation orders needs the permission to manage pricing.");
        }
    }

    /// <summary>A BOQ of the same project the caller may read, to copy lines from.</summary>
    private async Task<Boq> SourceAsync(UserAccess actor, long projectId, long sourceId, CancellationToken cancellationToken)
    {
        var source = await ReadableAsync(actor, sourceId, cancellationToken);
        return source.ProjectId == projectId ? source : throw new NotFoundException(BoqNotFound);
    }

    private Boq NewBoq(Project project, BoqKind kind, string title, string? reason, long authorId)
    {
        var now = clock.UtcNow;
        return new Boq
        {
            ProjectId = project.Id,
            Kind = kind,
            Status = BoqStatus.Draft,
            Title = title,
            Reason = reason,
            AuthorId = authorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Checks, numbers, totals and saves a new BOQ, with its first event when the client sees such BOQs.</summary>
    private async Task FinishAsync(Boq boq, Project project, long actorId, CancellationToken cancellationToken)
    {
        Throw(BoqRules.CheckContent(boq.Kind, boq.Reason, boq.AdjustmentType, boq.AdjustmentValue, LinesOf(boq)));
        Recompute(boq);
        if (BoqRules.PrefixOf(boq.Kind) is { } prefix)
        {
            var year = clock.CompanyToday.Year;
            boq.Number = BoqRules.Number(prefix, year, await boqs.NextSequenceAsync(prefix, year, cancellationToken));
        }

        boqs.Add(boq);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (boq.Kind != BoqKind.Internal)
        {
            activity.Add(BoqEvent(boq, project, ActivityEventKinds.BoqCreated, actorId, new { kind = KindOf(boq), number = boq.Number }));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Sets the work price and records the change as a fee change always was: in the payment history and the feed.</summary>
    /// <summary>
    /// The profit before a change of the work price or the permits, when the floor has to judge it: only a lower price can
    /// take the profit to zero, so a rise (the first agreement included) never needs forcing.
    /// </summary>
    private async Task<ProfitBefore?> FloorBeforeAsync(Project project, decimal work, decimal permits, CancellationToken cancellationToken)
        => work < (project.ProjectFee ?? 0) || permits < (project.PermitsPrice ?? 0) ? await floor.BeforeAsync(project, cancellationToken) : null;

    /// <summary>Sets the work price and the permits: one row of the price history, and an event for each that changed.</summary>
    private void SetPrice(Project project, decimal work, decimal permits, string description, long actorId)
    {
        var previous = project.ProjectFee ?? 0;
        var previousPermits = project.PermitsPrice ?? 0;
        if (work == previous && permits == previousPermits)
        {
            return;
        }

        var now = clock.UtcNow;
        project.PaymentChanges.Add(new ProjectPaymentChange
        {
            Amount = work,
            PreviousAmount = previous,
            TvaRate = project.TvaRate,
            PreviousTvaRate = project.TvaRate,
            SupervisionRate = project.SupervisionRate,
            PreviousSupervisionRate = project.SupervisionRate,
            Permits = permits,
            PreviousPermits = previousPermits,
            Description = description,
            ChangedAt = now,
            ChangedById = actorId,
        });
        if (work != previous)
        {
            activity.Add(PriceEvent(project, ActivityEventKinds.ProjectFeeSet, work, previous, actorId, now));
        }

        if (permits != previousPermits)
        {
            activity.Add(PriceEvent(project, ActivityEventKinds.PermitsSet, permits, previousPermits, actorId, now));
        }

        project.ProjectFee = work;
        project.PermitsPrice = permits;
        project.UpdatedAt = now;
    }

    private static ActivityEvent PriceEvent(Project project, string kind, decimal amount, decimal previous, long actorId, DateTime now) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = now,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "project",
        SubjectId = project.Id,
        Amount = amount,
        PrevAmount = previous,
    };

    private ActivityEvent BoqEvent(Boq boq, Project project, string kind, long actorId, object payload) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        ClientId = project.ClientId,
        SubjectType = "boq",
        SubjectId = boq.Id,
        Amount = boq.TotalUsd + boq.PermitsUsd,
        Payload = JsonSerializer.Serialize(payload),
    };

    private static string KindOf(Boq boq) => SnakeCaseEnum.ToDb(boq.Kind);

    private static BoqLine CopyOf(BoqLine line, int position) => new()
    {
        Position = position,
        Nature = line.Nature,
        Code = line.Code,
        Description = line.Description,
        Quantity = line.Quantity,
        WasteRate = line.WasteRate,
        Unit = line.Unit,
        Rate = line.Rate,
    };

    private static List<BoqRules.Line> LinesOf(Boq boq) => boq.Lines.Select(l => new BoqRules.Line(l.Quantity, l.WasteRate, l.Rate, l.Nature)).ToList();

    /// <summary>The totals always come from the lines, never from the client.</summary>
    private static void Recompute(Boq boq)
    {
        var totals = BoqRules.TotalsOf(LinesOf(boq), boq.AdjustmentType, boq.AdjustmentValue);
        boq.SubtotalUsd = totals.Subtotal;
        boq.TotalUsd = totals.Total;
        boq.PermitsUsd = totals.Permits;
    }

    /// <summary>The totals without one occurrence of <paramref name="total"/>: the VO being withdrawn.</summary>
    private static IReadOnlyList<decimal> WithoutOne(IReadOnlyList<decimal> totals, decimal total)
    {
        var rest = totals.ToList();
        rest.Remove(total);
        return rest;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<long> UserIds(IEnumerable<Boq> list)
        => list.SelectMany(b => b.Deliveries.Select(d => d.SentById).Append(b.AuthorId).Append(b.AgreedById)).OfType<long>().Distinct().ToList();

    private async Task<BoqDetailResponse> DescribeAsync(Boq boq, UserAccess actor, CancellationToken cancellationToken)
    {
        var caller = CallerOf(actor, boq.Project.OwnerId);
        return Detail(boq, await boqs.NamesAsync(UserIds([boq]), cancellationToken), caller.CanSeeMoney || caller.CanManagePricing);
    }

    /// <summary>
    /// The caller's own internal draft, tracked, with its site locked: submitting, withdrawing and keeping are its author's
    /// alone. Someone else's draft doesn't exist for them.
    /// </summary>
    private async Task<(UserAccess Actor, Project Site, Boq Boq)> AuthorsDraftAsync(long boqId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken);
        var peek = await ReadableAsync(actor, boqId, cancellationToken);
        if (peek.Kind != BoqKind.Internal || peek.AuthorId != actor.UserId)
        {
            throw new NotFoundException(BoqNotFound);
        }

        var site = await LockedSiteAsync(actor, peek.ProjectId, cancellationToken);
        return (actor, site, await boqs.GetForUpdateAsync(boqId, cancellationToken) ?? throw new NotFoundException(BoqNotFound));
    }

    /// <summary>
    /// Someone else changed a submitted draft: its author hears, once per draft until they open it, with a link to see what
    /// changed. Company-level, so an author who doesn't see Execution still gets it.
    /// </summary>
    private async Task TellAuthorOfEditAsync(Boq boq, Project site, UserAccess actor, CancellationToken cancellationToken)
    {
        if (boq.Kind != BoqKind.Internal || boq.SubmittedAt is null || boq.AuthorId == actor.UserId)
        {
            return;
        }

        await notifications.SendDigestAsync(
            new NotificationDraft(NotificationKinds.BoqDraftEdited, actor.UserId, null, null, "boq", boq.Id,
                new { editorName = currentUser.FullName, title = boq.Title, projectId = site.Id, projectTitle = site.Title }),
            $"boq-edited:{boq.Id}",
            [boq.AuthorId],
            restartOnceRead: true,
            cancellationToken);
    }

    /// <summary>The lines as the rules compare them.</summary>
    private static List<BoqRules.SubmittedLine> SubmittedLinesOf(Boq boq)
        => boq.Lines.OrderBy(l => l.Position)
            .Select(l => new BoqRules.SubmittedLine(l.Id, l.Nature, l.Code, l.Description, l.Quantity, l.WasteRate, l.Unit, l.Rate))
            .ToList();

    private static IReadOnlyList<BoqRules.LineChange>? ChangesOf(Boq boq)
        => boq.SubmittedAt is null
            ? null
            : BoqRules.Changes(
                boq.SubmittedLines is { } json ? JsonSerializer.Deserialize<List<BoqRules.SubmittedLine>>(json) : null,
                SubmittedLinesOf(boq));

    /// <summary>A client BOQ or VO's PDF: only for callers who read those, so with every figure.</summary>
    private async Task<BoqDocument> DocumentAsync(Boq boq, CancellationToken cancellationToken)
    {
        var project = boq.Project;
        return new BoqDocument(
            Detail(boq, await boqs.NamesAsync(UserIds([boq]), cancellationToken), seesPrice: true),
            project.Title,
            project.Location,
            project.Client is { } client ? ClientRules.DisplayName(client.Name, client.Organisation) : string.Empty);
    }

    /// <summary>
    /// The work price as stored, which agreeing and withdrawing keep equal to the agreed BOQ and VOs: a site priced
    /// before BOQs keeps its typed-in price until its first agreement replaces it.
    /// </summary>
    private static BoqPriceResponse PriceOf(Project project, decimal agreedBoq, decimal agreedVos, decimal agreedPermits)
    {
        var work = project.ProjectFee ?? 0;
        var permits = project.PermitsPrice ?? 0;
        var price = ExecutionPricing.PriceOf(work, permits, project.SupervisionRate ?? 0, project.TvaRate);
        return new BoqPriceResponse(agreedBoq, agreedVos, work, project.SupervisionRate ?? 0, price.Supervision, project.TvaRate, price.Tva, price.Gross,
            agreedPermits, permits);
    }

    private static PersonRefResponse PersonOf(long id, IReadOnlyDictionary<long, string> names)
        => new(id, names.GetValueOrDefault(id, "Someone"));

    private static PersonRefResponse? PersonOf(long? id, IReadOnlyDictionary<long, string> names)
        => id is { } known ? PersonOf(known, names) : null;

    private static BoqLastDeliveryResponse? LastDeliveryOf(Boq boq)
        => boq.Deliveries.OrderByDescending(d => d.SentAt).ThenByDescending(d => d.Id)
            .Select(d => new BoqLastDeliveryResponse(d.Channel, d.SentAt)).FirstOrDefault();

    private static BoqSummaryResponse Summary(Boq boq, int lineCount, IReadOnlyDictionary<long, string> names) => new(
        boq.Id,
        boq.Kind,
        boq.Number,
        boq.Title,
        PersonOf(boq.AuthorId, names),
        boq.Status,
        boq.TotalUsd,
        lineCount,
        boq.Reason,
        boq.CreatedAt,
        boq.UpdatedAt,
        boq.AgreedAt,
        PersonOf(boq.AgreedById, names),
        LastDeliveryOf(boq),
        boq.PermitsUsd,
        boq.SubmittedAt,
        ChangesOf(boq) is { Count: > 0 });

    private static BoqDetailResponse Detail(Boq boq, IReadOnlyDictionary<long, string> names, bool seesPrice)
    {
        var project = boq.Project;
        var price = ExecutionPricing.PriceOf(boq.TotalUsd, boq.PermitsUsd, project.SupervisionRate ?? 0, project.TvaRate);
        return new BoqDetailResponse(
            boq.Id,
            boq.Kind,
            boq.Number,
            boq.Title,
            PersonOf(boq.AuthorId, names),
            boq.Status,
            boq.TotalUsd,
            boq.Lines.Count,
            boq.Reason,
            boq.CreatedAt,
            boq.UpdatedAt,
            boq.AgreedAt,
            PersonOf(boq.AgreedById, names),
            LastDeliveryOf(boq),
            boq.ProjectId,
            boq.AdjustmentType,
            boq.AdjustmentValue,
            boq.Lines.OrderBy(l => l.Position).Select(l => new BoqLineResponse(
                l.Id,
                l.Position,
                l.Nature,
                l.Code,
                l.Description,
                l.Quantity,
                l.WasteRate,
                BoqRules.FinalQuantity(l.Quantity, l.WasteRate),
                l.Unit,
                l.Rate,
                BoqRules.LineTotal(l.Quantity, l.WasteRate, l.Rate))).ToList(),
            boq.SubtotalUsd,
            boq.TotalUsd - boq.SubtotalUsd,
            seesPrice ? project.SupervisionRate ?? 0 : null,
            seesPrice ? price.Supervision : null,
            seesPrice ? project.TvaRate : null,
            seesPrice ? price.Tva : null,
            seesPrice ? price.Gross : null,
            boq.PermitsUsd,
            boq.Deliveries.OrderByDescending(d => d.SentAt).ThenByDescending(d => d.Id)
                .Select(d => new BoqDeliveryResponse(d.Id, d.Channel, d.Recipient, d.Note, d.TotalUsd, d.SentAt, PersonOf(d.SentById, names))).ToList(),
            boq.SubmittedAt,
            ChangesOf(boq)?.Select(c => new BoqLineChangeResponse(c.Kind, c.LineId, c.Description,
                c.Fields.Select(f => new BoqFieldChangeResponse(f.Field, f.Before, f.After)).ToList())).ToList());
    }

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
