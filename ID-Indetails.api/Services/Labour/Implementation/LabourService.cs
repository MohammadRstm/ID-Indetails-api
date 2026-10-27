using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Labour;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Labour.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Labour.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Labour.Implementation;

public sealed class LabourService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    ILabourRepository labour,
    IActivityEventRepository activity,
    INotificationSender notifications,
    ISiteTeamRepository team,
    IUnitOfWork unitOfWork,
    IClock clock) : ILabourService
{
    private const string ProjectNotFound = "Project not found.";

    /// <summary>Ground labour is recorded without a currency: daily pay is in USD.</summary>
    private const string Currency = PaymentRules.BaseCurrency;

    public async Task<LabourWeekListResponse> ListWeeksAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var (actor, project) = await VisibleSiteAsync(projectId, cancellationToken);
        var reported = (await labour.ListWeeksAsync(projectId, cancellationToken)).ToDictionary(r => r.Week.WeekStart);
        var lastDay = clock.ToCompanyDate(project.FinalizedAt ?? clock.UtcNow);

        var weeks = LabourRules.ReportableWeeks(project.StartDate, lastDay)
            .Union(reported.Keys)
            .OrderByDescending(w => w)
            .Select(week => reported.TryGetValue(week, out var r)
                ? new LabourWeekSummaryResponse(week, true, Total(r.Week), r.Week.Days.Sum(d => d.Workers),
                    new PersonRefResponse(r.Week.SubmittedById, r.SubmittedByName), r.Week.SubmittedAt)
                : new LabourWeekSummaryResponse(week, false, null, null, null, null))
            .ToList();

        return new LabourWeekListResponse(
            weeks,
            reported.Values.Sum(r => Total(r.Week)),
            reported.Count,
            Currency,
            IsOpen(project) && await CanReportAsync(actor, project.Id, cancellationToken));
    }

    public async Task<LabourWeekResponse> GetWeekAsync(long projectId, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        var (actor, project) = await VisibleSiteAsync(projectId, cancellationToken);
        return await DescribeWeekAsync(actor, project, weekStart, cancellationToken);
    }

    public async Task<LabourWeekResponse> SubmitWeekAsync(
        long projectId,
        DateOnly weekStart,
        SubmitLabourWeekRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenSiteAsync(actor, projectId, cancellationToken);
        Throw(SiteTeamRules.CheckOnTeam(await CanReportAsync(actor, project.Id, cancellationToken)));

        var today = clock.CompanyToday;
        var days = request.Days.Select(d => new LabourRules.Day(d.Date, d.Workers, d.DailyPay)).ToList();
        Throw(LabourRules.CheckWeek(weekStart, project.StartDate, today));
        Throw(LabourRules.CheckDays(days, weekStart, today));

        var week = await labour.GetWeekForUpdateAsync(projectId, weekStart, cancellationToken);
        var isNew = week is null;
        if (week is not null)
        {
            Throw(SiteRules.CheckCanChange(week.SubmittedById == actor.UserId, actor.Has(PermissionCodes.LabourReportManage)));
        }

        var stored = week?.Days.Select(d => new LabourRules.Day(d.WorkDate, d.Workers, d.DailyPay)).ToList() ?? [];
        switch (LabourResubmission.Decide(
            week is not null,
            LabourResubmission.SameReport(stored, week?.Note, days, request.Note),
            LabourResubmission.VersionOf(week?.SubmittedAt, week?.UpdatedAt),
            request.ExpectedVersion))
        {
            case LabourSubmission.Unchanged:
                // sent again (an offline retry, or the same save twice): nothing to record or tell anyone
                await transaction.CommitAsync(cancellationToken);
                return await DescribeWeekAsync(actor, project, weekStart, cancellationToken);
            case LabourSubmission.Conflict:
                throw new ConflictException(ErrorCodes.LabourWeekChanged, "Someone changed this week since you loaded it.");
        }

        decimal? previousTotal = null;
        if (week is null)
        {
            week = new LabourWeek { ProjectId = projectId, WeekStart = weekStart, SubmittedAt = clock.UtcNow, SubmittedById = actor.UserId };
            labour.AddWeek(week);
        }
        else
        {
            previousTotal = Total(week);
            week.UpdatedAt = LabourResubmission.ChangedAt(week.UpdatedAt ?? week.SubmittedAt, clock.UtcNow);
        }

        ReplaceDays(week, days);
        week.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(new ActivityEvent
        {
            DomainCode = project.DomainCode,
            Kind = isNew ? ActivityEventKinds.LabourWeekSubmitted : ActivityEventKinds.LabourWeekUpdated,
            OccurredAt = clock.UtcNow,
            ActorId = actor.UserId,
            ProjectId = project.Id,
            SubjectType = "labour_week",
            SubjectId = week.Id,
            Amount = Total(week),
            PrevAmount = previousTotal,
            Payload = JsonSerializer.Serialize(new { weekStart }),
        });
        await NotifyAsync(project, weekStart, isNew, actor.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeWeekAsync(actor, project, weekStart, cancellationToken);
    }

    public async Task<LabourDefaultsResponse> GetDefaultsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var (actor, project) = await VisibleSiteAsync(projectId, cancellationToken);
        return await DescribeDefaultsAsync(actor, project, cancellationToken);
    }

    public async Task<LabourDefaultsResponse> SetDefaultsAsync(
        long projectId,
        SetLabourDefaultsRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenSiteAsync(actor, projectId, cancellationToken);

        var today = clock.CompanyToday;
        var history = (await labour.ListDefaultsAsync(projectId, cancellationToken)).Select(r => ToRule(r.Default)).ToList();
        var inForce = LabourRules.DefaultsAt(history, today);

        if (inForce?.Workers != request.Workers || inForce.DailyPay != request.DailyPay)
        {
            // A second change on the same day replaces the first.
            var row = await labour.GetDefaultForUpdateAsync(projectId, today, cancellationToken);
            if (row is null)
            {
                row = new ProjectLabourDefault { ProjectId = projectId, EffectiveFrom = today, Currency = Currency };
                labour.AddDefault(row);
            }

            row.Workers = request.Workers;
            row.DailyPay = request.DailyPay;
            row.CreatedAt = clock.UtcNow;
            row.CreatedById = actor.UserId;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity.Add(new ActivityEvent
            {
                DomainCode = project.DomainCode,
                Kind = ActivityEventKinds.LabourDefaultsSet,
                OccurredAt = clock.UtcNow,
                ActorId = actor.UserId,
                ProjectId = project.Id,
                SubjectType = "project",
                SubjectId = project.Id,
                Amount = request.DailyPay,
                PrevAmount = inForce?.DailyPay,
                Payload = JsonSerializer.Serialize(new { workers = request.Workers, previousWorkers = inForce?.Workers, effectiveFrom = today }),
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeDefaultsAsync(actor, project, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<(UserAccess Actor, Project Project)> VisibleSiteAsync(long projectId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution projects have ground labour.");
        }

        return (actor, project);
    }

    /// <summary>Locks the project row, so a change can't slip in while it is being finalised.</summary>
    private async Task<Project> OpenSiteAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        return project;
    }

    private async Task<LabourWeekResponse> DescribeWeekAsync(UserAccess actor, Project project, DateOnly weekStart, CancellationToken cancellationToken)
    {
        var record = await labour.GetWeekAsync(project.Id, weekStart, cancellationToken);
        var open = IsOpen(project) && await CanReportAsync(actor, project.Id, cancellationToken);

        if (record is null)
        {
            Throw(LabourRules.CheckWeek(weekStart, project.StartDate, clock.CompanyToday));
            var history = (await labour.ListDefaultsAsync(project.Id, cancellationToken)).Select(r => ToRule(r.Default)).ToList();
            var days = LabourRules.Prefill(weekStart, clock.CompanyToday, history);
            return new LabourWeekResponse
            {
                WeekStart = weekStart,
                IsReported = false,
                Days = days.Select(d => new LabourDayResponse(d.Date, d.Workers, d.DailyPay, d.Cost)).ToList(),
                Total = days.Sum(d => d.Cost),
                Currency = Currency,
                CanEdit = open,
                Version = LabourResubmission.UnreportedVersion,
            };
        }

        var week = record.Week;
        return new LabourWeekResponse
        {
            WeekStart = weekStart,
            IsReported = true,
            Days = week.Days.OrderBy(d => d.WorkDate)
                .Select(d => new LabourDayResponse(d.WorkDate, d.Workers, d.DailyPay, d.Workers * d.DailyPay))
                .ToList(),
            Total = Total(week),
            Currency = Currency,
            Note = week.Note,
            SubmittedBy = new PersonRefResponse(week.SubmittedById, record.SubmittedByName),
            SubmittedAt = week.SubmittedAt,
            UpdatedAt = week.UpdatedAt,
            CanEdit = open && SiteRules.CanChange(week.SubmittedById == actor.UserId, actor.Has(PermissionCodes.LabourReportManage)),
            Version = LabourResubmission.VersionOf(week.SubmittedAt, week.UpdatedAt),
        };
    }

    private async Task<LabourDefaultsResponse> DescribeDefaultsAsync(UserAccess actor, Project project, CancellationToken cancellationToken)
    {
        var rows = (await labour.ListDefaultsAsync(project.Id, cancellationToken)).OrderByDescending(r => r.Default.EffectiveFrom).ToList();
        var inForce = LabourRules.DefaultsAt(rows.Select(r => ToRule(r.Default)).ToList(), clock.CompanyToday);

        return new LabourDefaultsResponse(
            inForce is null ? null : ToResponse(rows.Single(r => r.Default.EffectiveFrom == inForce.EffectiveFrom)),
            rows.Select(ToResponse).ToList(),
            IsOpen(project) && actor.Has(PermissionCodes.LabourReportManage));
    }

    private async Task NotifyAsync(Project project, DateOnly weekStart, bool isNew, long actorId, CancellationToken cancellationToken)
        => await notifications.SendAsync(
            new NotificationDraft(
                NotificationKinds.LabourWeekSubmitted,
                actorId,
                project.DomainCode,
                project.Id,
                "project",
                project.Id,
                new { weekStart, updated = !isNew, projectTitle = project.Title }),
            await notifications.HoldersAsync(project.DomainCode, PermissionCodes.LabourReportManage, cancellationToken),
            cancellationToken);

    /// <summary>Updates the days kept, removes the ones left out and adds the new ones, so no day is ever duplicated.</summary>
    private void ReplaceDays(LabourWeek week, IReadOnlyList<LabourRules.Day> days)
    {
        foreach (var existing in week.Days.Where(d => days.All(n => n.Date != d.WorkDate)).ToList())
        {
            week.Days.Remove(existing);
            labour.RemoveDay(existing);
        }

        foreach (var day in days)
        {
            var row = week.Days.SingleOrDefault(d => d.WorkDate == day.Date);
            if (row is null)
            {
                row = new LabourWeekDay { WorkDate = day.Date };
                week.Days.Add(row);
            }

            row.Workers = day.Workers;
            row.DailyPay = day.DailyPay;
        }
    }

    /// <summary>The manage permission reports any site; the create permission only the sites the person oversees.</summary>
    private async Task<bool> CanReportAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
        => actor.Has(PermissionCodes.LabourReportManage)
            || (actor.Has(PermissionCodes.LabourReportCreate) && await team.IsOnTeamAsync(projectId, actor.UserId, cancellationToken));

    private static bool IsOpen(Project project)
        => SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null) is null;

    /// <summary>Worked out here rather than read from the generated column, which is only filled in after a save.</summary>
    private static decimal Total(LabourWeek week) => week.Days.Sum(d => d.Workers * d.DailyPay);

    private static LabourRules.Defaults ToRule(ProjectLabourDefault d) => new(d.EffectiveFrom, d.Workers, d.DailyPay);

    private static LabourDefaultResponse ToResponse(LabourDefaultRecord r)
        => new(r.Default.EffectiveFrom, r.Default.Workers, r.Default.DailyPay, r.Default.Currency,
            r.Default.CreatedById is { } id && r.SetByName is { } name ? new PersonRefResponse(id, name) : null,
            r.Default.CreatedAt);

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
