using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Reopening is the super admins' (<c>company_settings:manage</c>, checked by the endpoint). Reopening a month already
/// open moves its last day. Design staff are told when a month opens; ending it early tells no one.
/// </summary>
public sealed class ReopenedMonthService(
    ICurrentUser currentUser,
    IReopenedMonthRepository months,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IReopenedMonthService
{
    public async Task<IReadOnlyList<ReopenedMonthResponse>> OpenAsync(CancellationToken cancellationToken = default)
        => (await months.OpenAsync(clock.CompanyToday, cancellationToken)).Select(ToResponse).ToList();

    public async Task<ReopenedMonthResponse> ReopenAsync(string month, ReopenMonthRequest request, CancellationToken cancellationToken = default)
    {
        var first = MonthOf(month);
        ReopenRules.Check(first, request.OpenUntil, clock.CompanyToday).ThrowIfDenied();
        var note = request.Note?.Trim() ?? string.Empty;
        if (note.Length > 255)
        {
            RuleDenial.Invalid(ErrorCodes.ValidationFailed, "note", "Keep the note to 255 characters.").ThrowIfDenied();
        }

        var row = await months.GetAsync(first, cancellationToken);
        if (row is null)
        {
            row = new ReopenedMonth { Month = first };
            months.Add(row);
        }

        row.OpenUntil = request.OpenUntil;
        row.Note = note;
        row.ReopenedById = currentUser.Id;
        row.ReopenedAt = clock.UtcNow;
        Record(ActivityEventKinds.MonthReopened, first, request.OpenUntil);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.MonthReopened, currentUser.Id, DomainCodes.Design, null, "reopened_month", null,
                new
                {
                    month = first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    monthName = first.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                    openUntil = request.OpenUntil,
                    openUntilName = request.OpenUntil.ToString("MMM d", CultureInfo.InvariantCulture),
                }),
            await notifications.DomainMembersAsync(DomainCodes.Design, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await OpenAsync(cancellationToken)).Single(m => m.Month == month);
    }

    public async Task EndAsync(string month, CancellationToken cancellationToken = default)
    {
        var first = MonthOf(month);
        var row = await months.GetAsync(first, cancellationToken) ?? throw new NotFoundException("That month isn't reopened.");
        months.Remove(row);
        Record(ActivityEventKinds.MonthReopenEnded, first, row.OpenUntil);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static DateOnly MonthOf(string month)
        => DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)
            ? first
            : throw new NotFoundException("That month isn't reopened.");

    private void Record(string kind, DateOnly month, DateOnly openUntil)
        => activity.Add(new ActivityEvent
        {
            DomainCode = null,
            Kind = kind,
            OccurredAt = clock.UtcNow,
            ActorId = currentUser.Id,
            Payload = JsonSerializer.Serialize(new { month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture), openUntil }),
        });

    private static ReopenedMonthResponse ToResponse(ReopenedMonthView m)
        => new(m.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture), m.OpenUntil, m.Note, new PersonRefResponse(m.ReopenedById, m.ReopenedByName), m.ReopenedAt);
}
