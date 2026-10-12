using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Push.Implementation;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class PushQueueTests
{
    private readonly PushChannel _channel = new();

    private PushQueue Queue(bool enabled = true) => new(_channel, Options.Create(new PushOptions { Enabled = enabled }));

    private List<PushJob> Written()
    {
        var jobs = new List<PushJob>();
        while (_channel.Reader.TryRead(out var job))
        {
            jobs.Add(job);
        }

        return jobs;
    }

    [Fact]
    public async Task Flushing_hands_over_each_saved_notification_once_and_each_message_per_recipient()
    {
        var queue = Queue();
        var saved = new Notification { Id = 11, RecipientId = 2 };
        var rolledBack = new Notification { Id = 0, RecipientId = 3 };
        var digest = new Notification { Id = 12, RecipientId = 4 };

        queue.Notifications([saved, rolledBack]);
        queue.Notifications([saved]);
        queue.Notifications([digest], digest: true);
        queue.Message(50, 7, [2, 3, 3], [3]);
        queue.Reminder(2, PushKinds.LogReminder, PushTarget.OfPage("work"), null);
        await queue.FlushAsync();

        Written().ShouldBe(
        [
            new NotificationPushJob(2, 11, false),
            new NotificationPushJob(4, 12, true),
            new MessagePushJob(2, 50, 7, false),
            new MessagePushJob(3, 50, 7, true),
            new ReminderPushJob(2, PushKinds.LogReminder, PushTarget.OfPage("work"), null),
        ]);
    }

    [Fact]
    public async Task Nothing_is_handed_over_while_pushes_are_off_or_after_a_failed_request()
    {
        var off = Queue(enabled: false);
        off.Notifications([new Notification { Id = 1, RecipientId = 2 }]);
        await off.FlushAsync();

        var failed = Queue();
        failed.Message(50, null, [2], []);
        failed.Discard();
        await failed.FlushAsync();

        Written().ShouldBeEmpty();
    }
}
