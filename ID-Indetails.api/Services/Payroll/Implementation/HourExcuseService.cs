using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>Excused hours (see <c>Business/Payroll/ExcuseRules</c>). People outside the caller's domains read as not found.</summary>
public sealed class HourExcuseService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    IHourExcuseRepository excuses,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IHourExcuseService
{
    private const string NotFoundMessage = "Excuse not found.";

    public async Task<HourExcuseResponse> AddAsync(long userId, AddHourExcuseRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var user = await users.GetRoleSnapshotAsync(userId, cancellationToken);
        if (user is null || !EmployeeRules.CanSee(actor, user.DomainCode))
        {
            throw new NotFoundException("Employee not found.");
        }

        var month = DateOnly.ParseExact(request.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = request.Kind == ExcuseKind.Days ? request.Days.Distinct().Order().ToList() : [];
        ExcuseRules.Check(month, clock.CompanyToday, request.Kind, days).ThrowIfDenied();

        var excuse = new HourExcuse
        {
            UserId = user.Id,
            MonthStart = month,
            Kind = request.Kind,
            Minutes = request.Kind == ExcuseKind.Minutes ? request.Minutes : null,
            IsPaid = request.IsPaid,
            Note = string.IsNullOrWhiteSpace(request.Note) ? ExcuseRules.DefaultNote(request.Kind, request.IsPaid) : request.Note.Trim(),
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
            Days = days.Select(d => new HourExcuseDay { Day = d }).ToList(),
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        excuses.Add(excuse);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Record(user.DomainCode, excuse, ActivityEventKinds.HoursExcused, actor.UserId);
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.HoursExcused, actor.UserId, null, null, "hour_excuse", excuse.Id,
                new { month = request.Month, kind = excuse.Kind, minutes = excuse.Minutes, days, isPaid = excuse.IsPaid }),
            [user.Id],
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(excuse);
    }

    public async Task DeleteAsync(long excuseId, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var excuse = await excuses.GetForUpdateAsync(excuseId, cancellationToken);
        var user = excuse is null ? null : await users.GetRoleSnapshotAsync(excuse.UserId, cancellationToken);
        if (excuse is null || excuse.DeletedAt is not null || user is null || !EmployeeRules.CanSee(actor, user.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        excuse.DeletedAt = clock.UtcNow;
        excuse.DeletedById = actor.UserId;
        Record(user.DomainCode, excuse, ActivityEventKinds.HoursExcuseRemoved, actor.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public static HourExcuseResponse ToResponse(HourExcuse x)
        => new(x.Id, x.MonthStart, x.Kind, x.Minutes, x.Days.Select(d => d.Day).Order().ToList(), x.IsPaid, x.Note, x.CreatedAt, x.CreatedById);

    private void Record(string? domain, HourExcuse excuse, string kind, long actorId)
        => activity.Add(new ActivityEvent
        {
            DomainCode = domain,
            Kind = kind,
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            SubjectType = "user",
            SubjectId = excuse.UserId,
            // how much was excused, so the feed can say it
            Payload = JsonSerializer.Serialize(new
            {
                excuseId = excuse.Id, month = excuse.MonthStart, kind = excuse.Kind.ToString(), minutes = excuse.Minutes, days = excuse.Days.Count, isPaid = excuse.IsPaid,
            }),
        });
}
