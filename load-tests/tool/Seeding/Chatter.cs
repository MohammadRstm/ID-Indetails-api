using ID_Indetails.api.DTO.Request.Messaging;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Messages and reading. On working days each team chat gets a handful of messages, some mentioning a teammate, and a
/// few people message a colleague directly; later most of them read their chats. Every morning some people clear
/// their notifications, admins more often than employees.
/// </summary>
public sealed class Chatter(World world, ApiClient api, SimulatedClock clock)
{
    private Random Random => world.Random;

    public async Task MorningAsync(DateOnly day)
    {
        clock.MoveTo(day, 8, 45);
        foreach (var person in world.Working(day, _ => true).Where(p => Random.Chance(p.IsEmployee ? 0.3 : 0.5)))
        {
            await api.PostAsync(person.Token, "/api/notifications/read-all");
            world.Tally.Add("notification_reads");
        }
    }

    public async Task AfternoonAsync(DateOnly day)
    {
        if (!World.IsWorkingDay(day))
        {
            return;
        }

        clock.MoveTo(day, 16);
        var readers = new List<(Person Person, string Path)>();
        foreach (var (teamId, domain) in world.Teams)
        {
            var members = world.Working(day, p => p.TeamId == teamId || (p.IsAdmin && p.Domain == domain));
            if (members.Count < 2)
            {
                continue;
            }

            for (var i = Random.Next(1, 7); i > 0; i--)
            {
                var sender = Random.Pick(members);
                var mention = Random.Chance(0.1) ? Random.Pick(members.Where(m => m != sender).ToList()) : null;
                await api.PostAsync(sender.Token, $"/api/messages/teams/{teamId}", new SendMessageRequest
                {
                    Body = mention is null ? Random.Pick(Vocabulary.ChatLines) : $"@{mention.FirstName} {mention.LastName} {Random.Pick(Vocabulary.ChatLines)}",
                    MentionIds = mention is null ? [] : [mention.Id],
                });
                world.Tally.Add("messages");
            }

            readers.AddRange(members.Where(_ => Random.Chance(0.5)).Select(m => (m, $"/api/messages/teams/{teamId}/read")));
        }

        foreach (var sender in world.Working(day, _ => true).Where(_ => Random.Chance(0.06)))
        {
            // Someone of their own domain or a super admin; super admins write to anyone.
            var colleagues = world.Working(day, p => p != sender && (sender.Domain is null || p.Domain == sender.Domain || p.Domain is null));
            var to = Random.Pick(colleagues);
            await api.PostAsync(sender.Token, $"/api/messages/direct/{to.Id}", new SendMessageRequest { Body = Random.Pick(Vocabulary.DirectLines) });
            world.Tally.Add("messages");
            if (Random.Chance(0.7))
            {
                readers.Add((to, $"/api/messages/direct/{sender.Id}/read"));
            }
        }

        clock.MoveTo(day, 17, 30);
        foreach (var (person, path) in readers)
        {
            await api.PostAsync(person.Token, path);
        }
    }
}
