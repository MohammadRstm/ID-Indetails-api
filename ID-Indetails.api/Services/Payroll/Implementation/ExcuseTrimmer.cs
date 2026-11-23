using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>
/// Only Design employees have an hours ledger, so only their excuses give way. An excuse that gives way is removed and,
/// when some of it is left, replaced by one with what is left, as given by the same person: both stay in history. Whoever
/// gave it is told; a whole-month excuse stays as it is, and whoever gave it is told so they can decide.
/// </summary>
public sealed class ExcuseTrimmer(
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    IHourExcuseRepository excuses,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IExcuseTrimmer
{
    public async Task TrimAsync(long userId, DateTime startUtc, DateTime endUtc, long actorId, CancellationToken cancellationToken = default)
    {
        var person = (await payroll.UsersAsync([userId], cancellationToken)).SingleOrDefault();
        var startDay = clock.ToCompanyDate(startUtc);
        var month = new DateOnly(startDay.Year, startDay.Month, 1);
        if (person is null || !PayrollInputs.LedgerApplies(person))
        {
            return;
        }

        var personMonth = (await inputs.ForAsync([person], [month], cancellationToken)).Single();
        var live = personMonth.Excuses.Where(e => e.DeletedAt is null).ToList();
        if (live.Count == 0)
        {
            return;
        }

        var input = personMonth.Input;
        var splits = Overtime.Of(input.Work, input.Calendar);
        var normalByDay = splits.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => s.Normal));
        var lastDay = clock.ToCompanyDate(endUtc.AddTicks(-1));
        var worked = new[] { startDay, lastDay }.Where(d => normalByDay.GetValueOrDefault(d) > 0).ToHashSet();
        // the whole month's required minutes, as if it had ended: an excuse planned for later in the month stays
        var expected = HoursLedger.Build(month, month.AddMonths(1), input.JoinedOn, input.Calendar, splits,
            new HoursLedger.Excuses(false, 0, new HashSet<DateOnly>()), input.LeftOn, input.Gaps).Expected;

        var whole = live.FirstOrDefault(e => e.Kind == ExcuseKind.WholeMonth);
        if (whole is not null)
        {
            await TellAsync(whole, actorId, month, wholeMonth: true, [], 0, cancellationToken);
            return;
        }

        var changes = ExcuseTrim.Of(
            live.Select(e => new ExcuseTrim.Excuse(e.Id, e.Kind, e.Minutes, e.Days.Select(d => d.Day).ToList(), e.CreatedAt)).ToList(),
            worked,
            splits.Sum(s => s.Normal),
            expected,
            day => input.Calendar.HoursOn(day)?.Required ?? 0);

        foreach (var change in changes)
        {
            var excuse = await excuses.GetForUpdateAsync(change.ExcuseId, cancellationToken);
            if (excuse is null || excuse.DeletedAt is not null)
            {
                continue;
            }

            excuse.DeletedAt = clock.UtcNow;
            excuse.DeletedById = actorId;
            HourExcuse? replacement = null;
            if (!change.Removed)
            {
                replacement = new HourExcuse
                {
                    UserId = excuse.UserId,
                    MonthStart = excuse.MonthStart,
                    Kind = excuse.Kind,
                    Minutes = change.Minutes,
                    IsPaid = excuse.IsPaid,
                    Note = excuse.Note,
                    CreatedAt = clock.UtcNow,
                    CreatedById = excuse.CreatedById,
                    Days = change.Days.Select(d => new HourExcuseDay { Day = d }).ToList(),
                };
                excuses.Add(replacement);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            activity.Add(new ActivityEvent
            {
                DomainCode = person.DomainCode,
                Kind = ActivityEventKinds.HoursExcuseReduced,
                OccurredAt = clock.UtcNow,
                ActorId = actorId,
                SubjectType = "user",
                SubjectId = excuse.UserId,
                Payload = JsonSerializer.Serialize(new
                {
                    excuseId = excuse.Id,
                    replacementId = replacement?.Id,
                    month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    kind = excuse.Kind.ToString(),
                    days = change.DaysTaken,
                    minutes = change.MinutesTaken,
                    removed = change.Removed,
                }),
            });
            await TellAsync(excuse, actorId, month, wholeMonth: false, change.DaysTaken, change.MinutesTaken, cancellationToken);
        }
    }

    private async Task TellAsync(HourExcuse excuse, long actorId, DateOnly month, bool wholeMonth, IReadOnlyList<DateOnly> days, int minutes, CancellationToken cancellationToken)
    {
        if (excuse.CreatedById is not { } giver)
        {
            return;
        }

        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.HoursExcuseReduced, actorId, null, null, "hour_excuse", excuse.Id,
                new { month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture), wholeMonth, days, minutes, personId = excuse.UserId }),
            [giver],
            cancellationToken);
    }
}
