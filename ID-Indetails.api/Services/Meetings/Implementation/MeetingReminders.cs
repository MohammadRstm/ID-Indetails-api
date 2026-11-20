using System.Globalization;
using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Repositories.Meetings.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Meetings.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Meetings.Implementation;

/// <summary>
/// Reminds a project meeting's invitees and a general meeting's attendees in the <see cref="MeetingRules.ReminderMinutes"/>
/// before it starts: a notification and its push, once per person and start time (<see cref="MeetingRules.ReminderKey"/>,
/// remembered in <c>notification_sent_keys</c>). So a restart neither misses nor repeats one, a moved meeting is reminded
/// again, and someone invited late still is. The notification scheduler runs it every minute.
/// </summary>
public sealed class MeetingReminders(IMeetingRepository meetings, INotificationSender sender, IClock clock) : IMeetingReminders
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        foreach (var meeting in await meetings.StartingAsync(now, now.AddMinutes(MeetingRules.ReminderMinutes), cancellationToken))
        {
            await sender.SendOnceAsync(
                new NotificationDraft(
                    NotificationKinds.MeetingSoon,
                    null,
                    meeting.Domain,
                    meeting.ProjectId,
                    "meeting",
                    meeting.Id,
                    new
                    {
                        projectTitle = meeting.ProjectTitle,
                        topic = meeting.Topic,
                        startsAt = meeting.StartsAt,
                        endsAt = meeting.EndsAt,
                        // for the push, which is written without the company's time zone
                        startTime = clock.ToCompanyTime(meeting.StartsAt).ToString("HH:mm", CultureInfo.InvariantCulture),
                    }),
                MeetingRules.ReminderKey(meeting.Id, meeting.StartsAt),
                meeting.Attendees.Select(a => a.Id),
                cancellationToken);
        }
    }
}
