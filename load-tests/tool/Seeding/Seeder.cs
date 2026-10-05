using System.Diagnostics;
using System.Text.Json;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Plays the company's months day by day through the real API: each day's morning, scheduled checks, project work,
/// sites, billing, chat and the evening's time logs, in that order. The same plan and random seed give the same data.
/// </summary>
public sealed class Seeder(LoadTestSettings settings, SeedPlan plan, string password, TextWriter output)
{
    public async Task<World> RunAsync()
    {
        var watch = Stopwatch.StartNew();
        var clock = new SimulatedClock(plan.FirstDay);
        var mailbox = new SeedMailbox();
        await using var host = new SeedingApi(settings.ConnectionString, clock, mailbox);
        using var http = host.CreateApiClient();
        var api = new ApiClient(http, clock);
        var world = new World(plan);

        var company = new Company(world, api, host, mailbox, clock, password);
        var portfolio = new Portfolio(world, api, clock);
        var sites = new Sites(world, api, clock);
        var billing = new Billing(world, api, clock);
        var chatter = new Chatter(world, api, clock);
        var design = new DesignWork(world, api, clock);

        output.WriteLine($"Seeding    {plan.Users} users, {plan.FirstDay:yyyy-MM-dd} to {plan.LastDay:yyyy-MM-dd}, random seed {plan.RandomSeed}");
        await company.SetUpAsync(plan.FirstDay);
        await portfolio.CreateFirstPortfolioAsync(plan.FirstDay);

        for (var day = plan.FirstDay; day <= plan.LastDay; day = day.AddDays(1))
        {
            await company.MorningAsync(day);
            await chatter.MorningAsync(day);
            clock.MoveTo(day, 9, 5);
            await host.RunScheduledChecksAsync();
            await portfolio.DaytimeAsync(day);
            await sites.DaytimeAsync(day);
            await billing.DaytimeAsync(day);
            await chatter.AfternoonAsync(day);
            await design.EveningAsync(day);

            if (day.DayOfWeek == DayOfWeek.Sunday || day == plan.LastDay)
            {
                output.WriteLine($"  to {day:yyyy-MM-dd}: {api.Requests} requests, {world.Tally["work_entries"]} work entries, {watch.Elapsed:hh\\:mm\\:ss}");
            }
        }

        if (clock.UtcNow > DateTime.UtcNow)
        {
            throw new SeedingException($"The simulation ran into the future ({clock.UtcNow:u}). Seed with an earlier --anchor.");
        }

        output.WriteLine($"Seeded     {api.Requests} requests in {watch.Elapsed:hh\\:mm\\:ss}; {mailbox.Received} emails kept in memory, none sent");
        return world;
    }

    /// <summary>
    /// Who the test users are, for the k6 scenarios: address, persona, ids and status. Never a password: that is
    /// <c>LT_USER_PASSWORD</c>, which every test user shares.
    /// </summary>
    public static async Task WriteRosterAsync(World world, string path)
    {
        var roster = new
        {
            anchor = world.Plan.Anchor,
            firstDay = world.Plan.FirstDay,
            users = world.People.Select(p => new
            {
                email = p.Email,
                persona = p.Persona.ToString(),
                role = p.Role,
                domain = p.Domain,
                userId = p.Id,
                teamId = p.TeamId,
                status = !p.Joined ? "not_joined" : p.LeavesOn is not null ? "deactivated" : "active",
                partTime = p.PartTime,
                joker = p.Joker,
            }),
            counts = world.Tally.Counts,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(roster, new JsonSerializerOptions { WriteIndented = true }));
    }
}
