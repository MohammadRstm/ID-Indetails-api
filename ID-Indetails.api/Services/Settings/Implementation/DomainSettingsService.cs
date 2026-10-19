using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Settings;
using ID_Indetails.api.DTO.Response.Settings;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Settings.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;

namespace ID_Indetails.api.Services.Settings.Implementation;

/// <summary>
/// Each domain's time-tracking and notification rules. No rows are seeded: a domain uses the defaults
/// until its admins first save. Settings of a domain the caller can't see read as not found.
/// </summary>
public sealed class DomainSettingsService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IDomainSettingsRepository settings,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IDomainSettingsService
{
    public async Task<DomainSettingsResponse> GetAsync(string domainCode, CancellationToken cancellationToken = default)
    {
        await EnsureVisibleAsync(domainCode, cancellationToken);
        return ToResponse(await CurrentAsync(domainCode, cancellationToken));
    }

    public async Task<DomainSettingsResponse> SaveAsync(
        string domainCode,
        SaveDomainSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureVisibleAsync(domainCode, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var row = await settings.GetForUpdateAsync(domainCode, cancellationToken);
        if (row is null)
        {
            row = new DomainSettings { DomainCode = domainCode };
            settings.Add(row);
        }

        row.EditWindow = request.EditWindow;
        row.AllowOverlap = request.AllowOverlap;
        row.AllowFutureEntries = request.AllowFutureEntries;
        row.RequireDescription = request.RequireDescription;
        row.TimerEnabled = request.TimerEnabled;
        row.LogReminder = request.LogReminder;
        row.NotifyEntry = request.NotifyEntry;
        row.NotifyProject = request.NotifyProject;
        row.NotifyIdle = request.NotifyIdle;
        row.WeeklySummary = request.WeeklySummary;
        row.HoldLogging = request.HoldLogging;
        row.AutoArchive = request.AutoArchive;
        row.UpdatedAt = clock.UtcNow;
        row.UpdatedById = actorId;

        activity.Add(new ActivityEvent
        {
            DomainCode = domainCode,
            Kind = ActivityEventKinds.DomainSettingsUpdated,
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            SubjectType = "domain_settings",
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(row);
    }

    public async Task<DomainSettings> CurrentAsync(string domainCode, CancellationToken cancellationToken = default)
        => await settings.GetAsync(domainCode, cancellationToken) ?? new DomainSettings { DomainCode = domainCode };

    /// <summary>The caller's id, once the domain is known to be one they can see.</summary>
    private async Task<long> EnsureVisibleAsync(string domainCode, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!DomainCodes.IsBusiness(domainCode) || !actor.CanSee(domainCode))
        {
            throw new NotFoundException("Domain not found.");
        }

        return actor.UserId;
    }

    private static DomainSettingsResponse ToResponse(DomainSettings s) => new(
        s.DomainCode,
        s.EditWindow,
        s.AllowOverlap,
        s.AllowFutureEntries,
        s.RequireDescription,
        s.TimerEnabled,
        s.LogReminder,
        s.NotifyEntry,
        s.NotifyProject,
        s.NotifyIdle,
        s.WeeklySummary,
        s.HoldLogging,
        s.AutoArchive,
        s.UpdatedAt);
}
