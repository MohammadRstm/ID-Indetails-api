using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>Collects a request's pushes and hands them to the in-process channel the sender reads. Nothing while pushes are off.</summary>
public sealed class PushQueue(PushChannel channel, IOptions<PushOptions> options) : IPushQueue
{
    private readonly List<(Notification Row, bool Digest)> _notifications = [];
    private readonly List<PushJob> _jobs = [];

    public void Notifications(IEnumerable<Notification> rows, bool digest = false)
        => _notifications.AddRange(rows.Select(row => (row, digest)));

    public void Message(long messageId, long? teamId, IEnumerable<long> recipientIds, IEnumerable<long> mentionedIds, bool announcement = false)
    {
        var mentioned = mentionedIds.ToHashSet();
        _jobs.AddRange(recipientIds.Distinct().Select(id => new MessagePushJob(id, messageId, teamId, mentioned.Contains(id), announcement)));
    }

    public void Reminder(long userId, string kind, PushTarget target, string? projectTitle)
        => _jobs.Add(new ReminderPushJob(userId, kind, target, projectTitle));

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (options.Value.Enabled)
        {
            // A row never saved (id 0) was rolled back with its request: there is nothing to push.
            foreach (var (row, digest) in _notifications.Where(n => n.Row.Id > 0).DistinctBy(n => n.Row.Id))
            {
                channel.Write(new NotificationPushJob(row.RecipientId, row.Id, digest));
            }

            foreach (var job in _jobs)
            {
                channel.Write(job);
            }
        }

        Discard();
        return Task.CompletedTask;
    }

    public void Discard()
    {
        _notifications.Clear();
        _jobs.Clear();
    }
}
