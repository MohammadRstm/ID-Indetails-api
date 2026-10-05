using ID_Indetails.api.DTO.Request.WorkEntries;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Design time logging. Each working evening every designer logs their day on the projects they're on: a few blocks
/// with gaps and usually a lunch break, sometimes a long day, now and then a day off sick. Design admins log some of
/// their own work too. Admins then comment on a few of the day's entries.
/// </summary>
public sealed class DesignWork(World world, ApiClient api, SimulatedClock clock)
{
    private readonly List<long> _today = [];

    private Random Random => world.Random;

    public async Task EveningAsync(DateOnly day)
    {
        if (!World.IsWorkingDay(day))
        {
            return;
        }

        // Late enough that the longest day (to 19:30) has ended: the API refuses work that ends in the future.
        clock.MoveTo(day, 19, 45);
        _today.Clear();
        foreach (var person in world.Working(day, p => p.Domain == "design"))
        {
            if (person.PartTime && day.DayOfWeek is not (DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday))
            {
                continue;
            }

            if (person.IsEmployee && Random.Chance(0.02))
            {
                world.Absences.Add((person, day));
                continue;
            }

            if (person.IsAdmin && !Random.Chance(0.4))
            {
                continue;
            }

            await LogDayAsync(person, day);
        }

        await CommentAsync(day);
    }

    private async Task LogDayAsync(Person person, DateOnly day)
    {
        var projects = ProjectsOf(person, day);
        if (projects.Count == 0)
        {
            return;
        }

        var minute = 9 * 60 + 5 * Random.Next(0, 6);
        var end = person.PartTime ? 13 * 60
            : person.IsAdmin ? minute + 60 * Random.Next(2, 5)
            : Random.Chance(0.06) ? 19 * 60 + 30
            : 17 * 60 + 15 * Random.Next(-2, 3);
        var hadLunch = person.PartTime || !Random.Chance(0.7);

        while (minute < end - 30)
        {
            var length = Math.Min(15 * Random.Next(3, 13), end - minute);
            if (!hadLunch && minute + length > 13 * 60)
            {
                length = Math.Max(30, 13 * 60 - minute);
            }

            var project = Random.Chance(0.7) ? projects[0] : Random.Pick(projects);
            var entry = await api.PostAsync(person.Token, "/api/work-entries", new SaveWorkEntryRequest
            {
                ProjectId = project.Id,
                Description = Random.Pick(Vocabulary.DesignWork),
                StartedAt = clock.At(day, 0, minute),
                EndedAt = clock.At(day, 0, minute + length),
            });
            _today.Add(entry.Id());
            world.Tally.Add("work_entries");

            minute += length + 5 * Random.Next(0, 5);
            if (!hadLunch && minute >= 13 * 60)
            {
                minute += 30 + 15 * Random.Next(0, 2);
                hadLunch = true;
            }
        }
    }

    /// <summary>The live Design projects they're on, the one they're on longest first; someone on none joins one.</summary>
    private List<SeedProject> ProjectsOf(Person person, DateOnly day)
    {
        var live = world.Live("design");
        var mine = live.Where(p => p.Team.Contains(person)).ToList();
        if (mine.Count == 0 && live.Count > 0)
        {
            var project = Random.Pick(live);
            project.Team.Add(person);
            mine.Add(project);
        }

        return mine;
    }

    private async Task CommentAsync(DateOnly day)
    {
        clock.MoveTo(day, 20, 30);
        foreach (var entryId in _today.Where(_ => Random.Chance(0.02)))
        {
            var admin = world.AnyAdmin("design", day);
            var sendToGroup = Random.Chance(0.15);
            await api.PostAsync(admin.Token, $"/api/work-entries/{entryId}/comments", new AddWorkEntryCommentRequest
            {
                Body = Random.Pick(Vocabulary.Comments), SendToGroup = sendToGroup,
            });
            world.Tally.Add("work_entry_comments");
            if (sendToGroup)
            {
                world.Tally.Add("messages");
            }
        }
    }
}
