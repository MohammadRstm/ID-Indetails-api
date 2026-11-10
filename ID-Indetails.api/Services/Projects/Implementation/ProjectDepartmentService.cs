using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

public sealed class ProjectDepartmentService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IProjectDepartmentRepository departments,
    IDesignVoRepository designVos,
    IProjectFinanceService finance,
    IBlockedChangeRepository blockedChanges,
    IBlockedChangeRecorder recorder,
    IUserRepository users,
    IWorkEntryService workEntries,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectDepartmentService
{
    private const string NotFoundMessage = "Project not found.";

    public async Task<ProjectDepartmentsResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(actor, DesignProject(actor, await projects.GetAsync(projectId, cancellationToken)), cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectDepartmentChangeResponse>> HistoryAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        DesignProject(actor, await projects.GetAsync(projectId, cancellationToken));
        return (await departments.ChangesAsync(projectId, cancellationToken))
            .Select(r => new ProjectDepartmentChangeResponse
            {
                Id = r.Change.Id,
                Kind = r.Change.Kind,
                Before = Read(r.Change.Before),
                After = Read(r.Change.After),
                Reason = r.Change.Reason,
                ChangedBy = new PersonRefResponse(r.Change.ChangedById, r.ChangedByName),
                ChangedAt = r.Change.ChangedAt,
            })
            .ToList();
    }

    public async Task<ProjectDepartmentsResponse> SetAsync(long projectId, SetProjectDepartmentsRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.ProjectEdit);
        Project project;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            project = DesignProject(actor, await projects.GetForUpdateAsync(projectId, cancellationToken));
            Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
            var chosen = request.DepartmentIds;
            var candidates = await departments.CandidatesAsync(chosen, cancellationToken);
            Throw(ProjectDepartmentRules.CheckChosen(project.DomainCode, chosen, candidates));

            var current = await departments.ListForUpdateAsync(project.Id, cancellationToken);
            if (current.Select(d => d.TeamId).ToHashSet().SetEquals(chosen))
            {
                return await DescribeAsync(actor, project, cancellationToken);
            }

            var removed = current.Where(d => !chosen.Contains(d.TeamId)).ToList();
            var withHours = (await departments.WithHoursAsync(project.Id, cancellationToken))
                .Concat((await designVos.AppliedAsync(project.Id, cancellationToken)).ByDepartment.Keys)
                .ToHashSet();
            Throw(ProjectDepartmentRules.CheckRemoval(removed.Where(d => withHours.Contains(d.TeamId)).Select(d => d.Team.Name).ToList()));

            var before = Snapshot(current.Select(d => (d.TeamId, d.Team.Name, d.Share)));
            foreach (var department in removed)
            {
                departments.Remove(department);
            }

            var after = current.Except(removed).Select(d => (d, d.Team.Name)).ToList();
            foreach (var added in candidates.Where(c => current.All(d => d.TeamId != c.Id)))
            {
                var department = new ProjectDepartment { ProjectId = project.Id, TeamId = added.Id, AddedAt = clock.UtcNow, AddedById = actor.UserId };
                departments.Add(department);
                after.Add((department, added.Name));
            }

            // a new split starts equal: whoever sets the money adjusts it
            after = after.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var shares = ProjectDepartmentRules.EqualShares(after.Count);
            for (var i = 0; i < after.Count; i++)
            {
                after[i].d.Share = shares[i];
            }

            departments.AddChange(new ProjectDepartmentChange
            {
                ProjectId = project.Id,
                Kind = ProjectDepartmentChangeKind.Departments,
                Before = before,
                After = Snapshot(after.Select(a => (a.d.TeamId, a.Name, a.d.Share))),
                ChangedById = actor.UserId,
                ChangedAt = clock.UtcNow,
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await ReleaseAsync(actor, project, cancellationToken);
        return await DescribeAsync(actor, project, cancellationToken);
    }

    public async Task<ProjectDepartmentsResponse> SetSharesAsync(long projectId, SetDepartmentSharesRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialWrite);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = DesignProject(actor, await projects.GetForUpdateAsync(projectId, cancellationToken));
        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));

        var current = await departments.ListForUpdateAsync(project.Id, cancellationToken);
        var shares = request.Shares.Select(s => (s.DepartmentId, s.Share)).ToList();
        Throw(ProjectDepartmentRules.CheckShares(current.Select(d => d.TeamId).ToList(), shares));
        if (current.All(d => shares.Single(s => s.DepartmentId == d.TeamId).Share == d.Share))
        {
            Throw(RuleDenial.Invalid(ErrorCodes.ValidationFailed, "shares", "The shares are already these: change one of them."));
        }

        var before = Snapshot(current.Select(d => (d.TeamId, d.Team.Name, d.Share)));
        foreach (var department in current)
        {
            department.Share = shares.Single(s => s.DepartmentId == department.TeamId).Share;
        }

        departments.AddChange(new ProjectDepartmentChange
        {
            ProjectId = project.Id,
            Kind = ProjectDepartmentChangeKind.Shares,
            Before = before,
            After = Snapshot(current.Select(d => (d.TeamId, d.Team.Name, d.Share))),
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            ChangedById = actor.UserId,
            ChangedAt = clock.UtcNow,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project, cancellationToken);
    }

    /// <summary>
    /// Logs the hours that waited for one of the project's departments now, as the people who logged them, through the
    /// same checks as any log; each person hears whether theirs went through. One that no longer fits (it overlaps
    /// newer hours, the month closed, the profit floor refuses it) isn't kept: its person is told why, to log it again.
    /// </summary>
    private async Task ReleaseAsync(UserAccess actor, Project project, CancellationToken cancellationToken)
    {
        var onProject = (await departments.ListAsync([project.Id], cancellationToken)).Select(d => d.TeamId).ToList();
        foreach (var waiting in await blockedChanges.PendingAsync(project.Id, BlockedChangeKind.WorkEntryDepartment, cancellationToken))
        {
            var person = await users.GetByIdAsync(waiting.RequestedById, cancellationToken);
            if (person is null || !ProjectDepartmentRules.MayLog(onProject, person.TeamId)
                || !await blockedChanges.DecideAsync(waiting.Id, BlockedChangeStatus.Forced, actor.UserId, clock.UtcNow, cancellationToken))
            {
                continue;
            }

            var request = JsonSerializer.Deserialize<BlockedRequest<SaveWorkEntryRequest>>(waiting.Payload, ProfitFloor.Json)!.Request;
            string? failure = null;
            try
            {
                currentUser.ActAs(person.Id, person.RoleCode, person.DomainCode, PersonName.Of(person.FirstName, person.LastName, person.Email));
                var entry = await workEntries.CreateAsync(request, cancellationToken);
                await blockedChanges.SetResultAsync(waiting.Id, entry.Id, cancellationToken);
            }
            catch (ProfitFloorException floor)
            {
                // kept again, now for whoever sees the project's money to decide
                await recorder.RecordAsync(floor, cancellationToken);
                failure = floor.Message;
            }
            catch (AppException refused)
            {
                unitOfWork.DiscardChanges();
                failure = refused.Message;
            }
            finally
            {
                currentUser.StopActing();
            }

            if (failure is not null)
            {
                await blockedChanges.ReopenAsync(waiting.Id, cancellationToken);
                await blockedChanges.DecideAsync(waiting.Id, BlockedChangeStatus.Dismissed, actor.UserId, clock.UtcNow, cancellationToken);
            }

            await notifications.SendDigestAsync(
                new NotificationDraft(NotificationKinds.WorkEntryReleased, actor.UserId, project.DomainCode, project.Id, "project", project.Id,
                    new { projectTitle = project.Title, released = failure is null, reason = failure }),
                $"log-released-{project.Id}-{(failure is null ? "yes" : "no")}",
                [person.Id],
                cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ProjectDepartmentsResponse> DescribeAsync(UserAccess actor, Project project, CancellationToken cancellationToken)
    {
        var rows = await departments.ListAsync([project.Id], cancellationToken);
        var ids = rows.Select(r => r.TeamId).ToList();
        var name = ProjectDepartmentRules.IsFullFile(ids, await departments.AllDesignAsync(cancellationToken)) ? ProjectDepartmentRules.FullFile : null;
        if (!actor.Has(PermissionCodes.FinancialView))
        {
            return new ProjectDepartmentsResponse
            {
                Name = name,
                Departments = rows.Select(r => new ProjectDepartmentResponse { Id = r.TeamId, Name = r.Name }).ToList(),
            };
        }

        var labour = await finance.LabourByDepartmentAsync(project, cancellationToken);
        // the price before VOs is split by the shares; each applied VO's amounts go to the departments it gave them to
        var vos = await designVos.AppliedAsync(project.Id, cancellationToken);
        var beforeVos = project.Revenue - vos.Total;
        var people = await departments.NamesAsync(labour.Select(l => l.UserId).Distinct().ToList(), cancellationToken);
        var other = labour.Where(l => l.TeamId is not { } team || !ids.Contains(team)).ToList();
        return new ProjectDepartmentsResponse
        {
            Name = name,
            Departments = rows.Select(r => new ProjectDepartmentResponse
            {
                Id = r.TeamId,
                Name = r.Name,
                Share = r.Share,
                Money = Money(labour.Where(l => l.TeamId == r.TeamId), Math.Round(beforeVos * r.Share, 2) + vos.ByDepartment.GetValueOrDefault(r.TeamId), people),
            }).ToList(),
            Other = other.Count > 0 ? Money(other, 0, people) : null,
        };
    }

    private static DepartmentMoneyResponse Money(IEnumerable<DepartmentLabour> labour, decimal budget, IReadOnlyDictionary<long, string> names)
    {
        var people = labour
            .GroupBy(l => l.UserId)
            .Select(g => new DepartmentPersonResponse
            {
                Person = new PersonRefResponse(g.Key, names.GetValueOrDefault(g.Key) ?? "Someone"),
                Minutes = g.Sum(l => l.Minutes),
                Cost = Math.Round(g.Sum(l => l.Cost), 2),
                Months = g.GroupBy(l => l.Month)
                    .OrderBy(m => m.Key)
                    .Select(m => new DepartmentMonthResponse(m.Key, m.Sum(l => l.Minutes), m.First().HourlyRate is { } rate ? Math.Round(rate, 2) : null,
                        Math.Round(m.Sum(l => l.Cost), 2)))
                    .ToList(),
            })
            .OrderByDescending(p => p.Cost)
            .ThenBy(p => p.Person.Name)
            .ToList();
        var cost = people.Sum(p => p.Cost);
        var unconverted = labour
            .SelectMany(l => l.Unconverted)
            .GroupBy(u => u.Currency)
            .Select(g => new CurrencyAmountResponse(g.Key, Math.Round(g.Sum(u => u.Amount), 2)))
            .OrderBy(u => u.Currency)
            .ToList();
        return new DepartmentMoneyResponse
        {
            Budget = budget, Cost = cost, Left = budget - cost, Minutes = people.Sum(p => p.Minutes), Unconverted = unconverted, People = people,
        };
    }

    /// <summary>The split as it is kept in the history: each department with its name then.</summary>
    public static string Snapshot(IEnumerable<(long TeamId, string Name, decimal Share)> split)
        => JsonSerializer.Serialize(split.Select(s => new DepartmentShareResponse(s.TeamId, s.Name, s.Share)), ProfitFloor.Json);

    private static IReadOnlyList<DepartmentShareResponse> Read(string? snapshot)
        => snapshot is null ? [] : JsonSerializer.Deserialize<List<DepartmentShareResponse>>(snapshot, ProfitFloor.Json) ?? [];

    /// <summary>A Design project the caller sees; Execution sites have no departments.</summary>
    private static Project DesignProject(UserAccess actor, Project? project)
    {
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        return project.DomainCode == DomainCodes.Design
            ? project
            : throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Design projects have departments.");
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
