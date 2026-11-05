using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

public sealed class DesignVoService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IProjectDepartmentRepository departments,
    IDesignVoRepository vos,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IProfitFloor floor,
    IClock clock) : IDesignVoService
{
    private const string NotFoundMessage = "Variation order not found.";

    public async Task<IReadOnlyList<DesignVoResponse>> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        DesignClientProject(actor, await projects.GetAsync(projectId, cancellationToken));
        return await DescribeAsync(await vos.ListAsync(projectId, cancellationToken), cancellationToken);
    }

    public async Task<OutsideScopeResponse> OutsideScopeAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        var project = DesignClientProject(actor, await projects.GetAsync(projectId, cancellationToken));
        var (from, minutes) = await OutsideScopeOfAsync(project, cancellationToken);
        return new OutsideScopeResponse(from, minutes);
    }

    public async Task<DesignVoResponse> CreateAsync(long projectId, CreateDesignVoRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialWrite);
        var lines = request.Lines.Select(l => new DesignVoRules.Line(l.Description.Trim(), l.Amount)).ToList();
        Throw(DesignVoRules.CheckLines(lines));
        var total = DesignVoRules.Total(lines);
        var now = clock.UtcNow;
        DesignVo vo;

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var project = DesignClientProject(actor, await projects.GetForUpdateAsync(projectId, cancellationToken));
            Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
            var split = (await departments.ListAsync([project.Id], cancellationToken)).Select(d => d.TeamId).ToList();
            var allocations = request.Allocations.Select(a => (a.DepartmentId, a.Amount)).ToList();
            Throw(DesignVoRules.CheckAllocations(request.Split, split, allocations, total));
            Throw(DesignVoRules.CheckPrice(project.Revenue, total));

            var outside = request.ShowOutsideScope ? await OutsideScopeOfAsync(project, cancellationToken) : ((DateOnly, int)?)null;
            var year = clock.CompanyToday.Year;
            vo = new DesignVo
            {
                ProjectId = project.Id,
                Number = DesignVoRules.Number(year, await vos.NextSequenceAsync(year, cancellationToken)),
                Title = request.Title.Trim(),
                Status = DesignVoStatus.Pending,
                Split = request.Split,
                TotalUsd = total,
                TvaRate = project.TvaRate,
                OutsideScopeFrom = outside?.Item1,
                OutsideScopeMinutes = outside?.Item2,
                CreatedAt = now,
                CreatedById = actor.UserId,
                // sent now for the client to confirm; one applied at once is never pending
                SentAt = request.Apply ? null : now,
                SentById = request.Apply ? null : actor.UserId,
            };
            for (var i = 0; i < lines.Count; i++)
            {
                vo.Lines.Add(new DesignVoLine { Position = i + 1, Description = lines[i].Description, Amount = lines[i].Amount });
            }

            foreach (var (teamId, amount) in allocations)
            {
                vo.Allocations.Add(new DesignVoAllocation { TeamId = teamId, Amount = amount });
            }

            vos.Add(vo);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // made first, then applied on its own: refused by the profit floor, it stays pending for whoever sees the money
        return request.Apply ? await ConfirmAsync(vo.Id, cancellationToken) : await DescribeAsync(vo.Id, cancellationToken);
    }

    public async Task<DesignVoResponse> ConfirmAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialWrite);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var vo = await vos.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        var project = DesignClientProject(actor, await projects.GetForUpdateAsync(vo.ProjectId, cancellationToken));
        Throw(DesignVoRules.CheckPending(vo.Status));
        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
        Throw(DesignVoRules.CheckPrice(project.Revenue, vo.TotalUsd));
        var before = await floor.BeforeAsync(project, cancellationToken);

        if (vo.Split == DesignVoSplit.Shares)
        {
            // the shares as they are now, kept: money already agreed doesn't move when they change later
            var shares = (await departments.ListAsync([project.Id], cancellationToken)).Select(d => (d.TeamId, d.Share)).ToList();
            foreach (var (teamId, amount) in DesignVoRules.ByShares(vo.TotalUsd, shares))
            {
                vo.Allocations.Add(new DesignVoAllocation { TeamId = teamId, Amount = amount });
            }
        }

        var now = clock.UtcNow;
        var price = project.Revenue + vo.TotalUsd;
        activity.Add(new ActivityEvent
        {
            DomainCode = project.DomainCode,
            Kind = project.Revenue > 0 ? ActivityEventKinds.RevenueChanged : ActivityEventKinds.RevenueSet,
            OccurredAt = now,
            ActorId = actor.UserId,
            ProjectId = project.Id,
            SubjectType = "project",
            SubjectId = project.Id,
            Amount = price,
            PrevAmount = project.Revenue,
        });
        project.PaymentChanges.Add(new ProjectPaymentChange
        {
            Amount = price,
            PreviousAmount = project.Revenue,
            TvaRate = project.TvaRate,
            PreviousTvaRate = project.TvaRate,
            Description = $"{vo.Number}: {vo.Title}",
            ChangedAt = now,
            ChangedById = actor.UserId,
        });
        project.Revenue = price;
        project.UpdatedAt = now;
        vo.Status = DesignVoStatus.Applied;
        vo.AppliedAt = now;
        vo.AppliedById = actor.UserId;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await floor.CheckAsync(before, BlockedChangeKind.DesignVo, new BlockedRequest<object?>(project.Id, null, ItemId: vo.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(vo.Id, cancellationToken);
    }

    public async Task<DesignVoResponse> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialWrite);
        var vo = await vos.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        DesignClientProject(actor, await projects.GetAsync(vo.ProjectId, cancellationToken));
        Throw(DesignVoRules.CheckPending(vo.Status));

        vo.Status = DesignVoStatus.Cancelled;
        vo.CancelledAt = clock.UtcNow;
        vo.CancelledById = actor.UserId;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(vo.Id, cancellationToken);
    }

    public async Task<DesignVoDocument> GetDocumentAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        var vo = await vos.GetAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        var project = DesignClientProject(actor, vo.Project);
        var client = project.Client is { } c ? ClientRules.DisplayName(c.Name, c.Organisation) : "Client";
        return new DesignVoDocument((await DescribeAsync([vo], cancellationToken))[0], project.Title, project.Location, client);
    }

    /// <summary>Since the latest applied VO, or the project's first day: the hours a new one can show.</summary>
    private async Task<(DateOnly From, int Minutes)> OutsideScopeOfAsync(Project project, CancellationToken cancellationToken)
    {
        var since = await vos.LastAppliedAtAsync(project.Id, cancellationToken) ?? clock.CompanyDayStartUtc(project.StartDate);
        return (clock.ToCompanyDate(since), await vos.OutsideScopeMinutesAsync(project.Id, since, cancellationToken));
    }

    private async Task<DesignVoResponse> DescribeAsync(long id, CancellationToken cancellationToken)
        => (await DescribeAsync([await vos.GetAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage)], cancellationToken))[0];

    private async Task<IReadOnlyList<DesignVoResponse>> DescribeAsync(IReadOnlyList<DesignVo> list, CancellationToken cancellationToken)
    {
        var people = await departments.NamesAsync(
            list.SelectMany(v => new[] { v.CreatedById, v.SentById, v.AppliedById, v.CancelledById }).OfType<long>().Distinct().ToList(),
            cancellationToken);
        PersonRefResponse? Person(long? id) => id is { } p ? new PersonRefResponse(p, people.GetValueOrDefault(p) ?? "Someone") : null;

        return list.Select(v =>
        {
            var tva = Math.Round(v.TotalUsd * v.TvaRate, 2, MidpointRounding.AwayFromZero);
            return new DesignVoResponse
            {
                Id = v.Id,
                ProjectId = v.ProjectId,
                Number = v.Number,
                Title = v.Title,
                Status = v.Status,
                Split = v.Split,
                Lines = v.Lines.OrderBy(l => l.Position).Select(l => new DesignVoLineResponse(l.Id, l.Description, l.Amount)).ToList(),
                Allocations = v.Allocations.OrderBy(a => a.Team.Name).Select(a => new DesignVoAllocationResponse(a.TeamId, a.Team.Name, a.Amount)).ToList(),
                TotalUsd = v.TotalUsd,
                TvaRate = v.TvaRate,
                TvaUsd = tva,
                TotalWithTvaUsd = v.TotalUsd + tva,
                OutsideScopeMinutes = v.OutsideScopeMinutes,
                OutsideScopeFrom = v.OutsideScopeFrom,
                CreatedBy = Person(v.CreatedById)!,
                CreatedAt = v.CreatedAt,
                SentBy = Person(v.SentById),
                SentAt = v.SentAt,
                AppliedBy = Person(v.AppliedById),
                AppliedAt = v.AppliedAt,
                CancelledBy = Person(v.CancelledById),
                CancelledAt = v.CancelledAt,
            };
        }).ToList();
    }

    /// <summary>A Design client project the caller sees: Execution has its BOQ variation orders, internal work no price.</summary>
    private static Project DesignClientProject(UserAccess actor, Project? project)
    {
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        if (project.DomainCode != DomainCodes.Design)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Execution sites change their price with BOQ variation orders.");
        }

        Throw(ProjectRules.CheckHasPayment(project.BillingType));
        return project;
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
