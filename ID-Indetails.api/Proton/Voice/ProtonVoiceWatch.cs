using System.Text.Json.Nodes;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Proton.Prompts;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Voice;

/// <summary>
/// The server's watch over each voice session, on its own connection to the provider: it reads the session's usage,
/// closes it when it reaches its time, when the budget runs out, when the person ends it or when the app stops, and
/// records what it cost once. If the provider never reports a final figure, the cost is estimated from the time the
/// session was held open, and the record says it is an estimate.
/// </summary>
public sealed class ProtonVoiceWatch(
    IProtonVoiceProvider provider,
    ProtonVoiceSessions sessions,
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<ProtonOptions> options,
    ILoggerFactory loggers) : IHostedService
{
    /// <summary>The provider bills this much when a WebRTC session starts, whatever happens next.</summary>
    internal const int MinimumBilledSeconds = 15;

    private static readonly TimeSpan BudgetCheckEvery = TimeSpan.FromSeconds(30);

    private readonly ILogger _log = loggers.CreateLogger(Tools.ProtonToolbox.LogCategory);
    private readonly Lock _lock = new();
    private readonly List<Task> _running = [];

    private ProtonVoiceOptions Voice => options.Value.Voice;

    /// <summary>Starts watching a session the provider has just started.</summary>
    public void Watch(ProtonVoiceSession session)
    {
        var task = Task.Run(() => RunAsync(session));
        lock (_lock)
        {
            _running.RemoveAll(t => t.IsCompleted);
            _running.Add(task);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Closes every session still running, so none is left paid for after the app stops.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in sessions.All())
        {
            session.Close();
        }

        Task[] running;
        lock (_lock)
        {
            running = [.. _running];
        }

        await Task.WhenAny(Task.WhenAll(running), Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task RunAsync(ProtonVoiceSession session)
    {
        var tally = new Tally();
        try
        {
            await using var link = await provider.AttachAsync(session.SessionId, session.Closing);
            var reading = ReadAsync(link, session, tally);
            var due = session.EndsAt - clock.UtcNow;
            var closeDue = Task.Delay(due > TimeSpan.Zero ? due : TimeSpan.Zero, session.Closing);
            if (await Task.WhenAny(reading, closeDue) != reading)
            {
                // its time is up, or it should close: ask the provider, and wait for the final figure
                await SendCloseAsync(link);
                await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(Voice.CloseWaitSeconds)));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Without the watch the session can't be closed from here: hold it to its time, then estimate.
            _log.LogWarning(exception, "Proton voice session {SessionId}: the server couldn't watch it.", session.SessionId);
            var due = session.EndsAt - clock.UtcNow;
            try
            {
                await Task.Delay(due > TimeSpan.Zero ? due : TimeSpan.Zero, session.Closing);
            }
            catch (OperationCanceledException)
            {
                // ended earlier
            }
        }
        finally
        {
            sessions.Release(session);
            await RecordAsync(session, tally);
        }
    }

    private async Task ReadAsync(IProtonVoiceLink link, ProtonVoiceSession session, Tally tally)
    {
        try
        {
            await ReadUntilClosedAsync(link, session, tally);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // the connection dropped or was closed under it: what was read so far stands, unconfirmed
            _log.LogInformation("Proton voice session {SessionId}: the watch connection closed before the final usage.", session.SessionId);
        }
    }

    private async Task ReadUntilClosedAsync(IProtonVoiceLink link, ProtonVoiceSession session, Tally tally)
    {
        var budgetCheckedAt = DateTime.MinValue;
        while (await link.ReceiveAsync(CancellationToken.None) is { } message)
        {
            switch (message["type"]?.GetValue<string>())
            {
                case "session.usage.updated":
                    tally.Seconds = Math.Max(tally.Seconds, SecondsOf(message));
                    if (clock.UtcNow - budgetCheckedAt >= BudgetCheckEvery)
                    {
                        budgetCheckedAt = clock.UtcNow;
                        if (await BudgetLeftAsync() <= CostOf(tally.Seconds))
                        {
                            _log.LogInformation("Proton voice session {SessionId} closed: the month's budget is used.", session.SessionId);
                            session.Close();
                        }
                    }

                    break;
                case "session.closed":
                    tally.Seconds = Math.Max(tally.Seconds, SecondsOf(message));
                    tally.Reason = message["reason"]?.GetValue<string>();
                    tally.Confirmed = true;
                    return;
            }
        }
    }

    private static async Task SendCloseAsync(IProtonVoiceLink link)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await link.SendAsync(new JsonObject { ["type"] = "session.close" }, timeout.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // the connection is gone; the wait below ends the watch
        }
    }

    private async Task<decimal> BudgetLeftAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProtonUsageService>().BudgetRemainingAsync();
    }

    private async Task RecordAsync(ProtonVoiceSession session, Tally tally)
    {
        // never less than the start-up charge; unconfirmed, as long as it was held open
        var held = (int)Math.Ceiling((clock.UtcNow - session.StartedAt).TotalSeconds);
        var seconds = Math.Max(MinimumBilledSeconds, tally.Confirmed ? tally.Seconds : Math.Max(tally.Seconds, held));
        var row = new ProtonUsage
        {
            UserId = session.UserId,
            ConversationId = session.ConversationId,
            OccurredAt = session.StartedAt,
            Model = Voice.Model,
            PromptVersion = ProtonPrompts.VoiceVersion,
            Outcome = tally.Confirmed ? ProtonOutcome.Answered : ProtonOutcome.Failed,
            Channel = ProtonChannel.Voice,
            VoiceSeconds = seconds,
            CostUsd = CostOf(seconds),
            Question = "[voice session]",
            Answer = tally.Confirmed ? $"[ended: {tally.Reason ?? "unknown"}]" : "[no final usage from the provider: estimated from the time it was open]",
        };
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ProtonUsageService>().RecordAsync(row, CancellationToken.None);
            _log.LogInformation("Proton voice session {SessionId} ended after {Seconds} s ({Confirmed}), about {CostUsd} USD.",
                session.SessionId, seconds, tally.Confirmed ? "reported" : "estimated", row.CostUsd);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log.LogError(exception, "Proton voice session {SessionId}: its usage couldn't be recorded.", session.SessionId);
        }
    }

    private decimal CostOf(int seconds) => Math.Round(seconds * Voice.PricePerMinuteUsd / 60m, 6);

    private static int SecondsOf(JsonObject message)
        => message["usage"]?["seconds"] is JsonValue value && value.TryGetValue(out double seconds) ? (int)Math.Ceiling(seconds) : 0;

    private sealed class Tally
    {
        public int Seconds { get; set; }

        public string? Reason { get; set; }

        public bool Confirmed { get; set; }
    }
}
