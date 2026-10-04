using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.Enums;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Execution sites. The crew records material deliveries most days (Monday to Saturday) and reports last week's
/// ground labour every Monday. Every few weeks the project's admin sets a client price or two and sends the client a
/// statement of the materials supplied.
/// </summary>
public sealed class Sites(World world, ApiClient api, SimulatedClock clock)
{
    private Random Random => world.Random;

    public async Task DaytimeAsync(DateOnly day)
    {
        if (day.DayOfWeek == DayOfWeek.Sunday)
        {
            return;
        }

        clock.MoveTo(day, 10, 30);
        if (day.DayOfWeek == DayOfWeek.Monday)
        {
            foreach (var site in world.Live("execution").Where(p => p.StartedOn < day.AddDays(-1)))
            {
                await ReportLabourAsync(site, day);
            }
        }

        clock.MoveTo(day, 11);
        foreach (var site in world.Live("execution").Where(_ => Random.Chance(0.45)))
        {
            await RecordMaterialsAsync(site, day);
        }

        if (World.IsWorkingDay(day))
        {
            foreach (var site in world.Live("execution").Where(p => DueForStatement(p, day)))
            {
                await SendStatementAsync(site, day);
            }
        }
    }

    private async Task RecordMaterialsAsync(SeedProject site, DateOnly day)
    {
        var crew = site.Team.Where(p => p.WorksOn(day)).ToList();
        if (crew.Count == 0)
        {
            return;
        }

        var items = new List<NewMaterialRequest>();
        var costs = new List<decimal>();
        for (var i = Random.Next(1, 5); i > 0; i--)
        {
            var (name, unit, low, high) = Random.Pick(Vocabulary.Materials);
            if (Random.Chance(0.2))
            {
                var amount = Random.Amount(100m, 3000m, 10m);
                items.Add(new NewMaterialRequest { Name = name, Amount = amount, Supplier = Random.Pick(Vocabulary.Suppliers), Note = "LT lump sum" });
                costs.Add(amount);
            }
            else
            {
                var quantity = unit == "tonne" ? Random.Amount(0.5m, 5m, 0.5m) : Random.Next(2, 121);
                var unitCost = Random.Amount(low, high, 0.5m);
                items.Add(new NewMaterialRequest { Name = name, Quantity = quantity, Unit = unit, UnitCost = unitCost, Supplier = Random.Pick(Vocabulary.Suppliers) });
                costs.Add(decimal.Round(quantity * unitCost, 2));
            }
        }

        var added = await api.PostAsync(Random.Pick(crew).Token, $"/api/projects/{site.Id}/materials", new AddMaterialsRequest { Items = items });
        var ids = added.EnumerateArray().Select(m => m.Id()).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            site.Unstated.Add((ids[i], costs[i]));
            site.MaterialCost += costs[i];
        }

        world.Tally.Add("materials", ids.Count);
    }

    /// <summary>Last week, Monday to Saturday from the site's first day, around the project's usual crew.</summary>
    private async Task ReportLabourAsync(SeedProject site, DateOnly day)
    {
        var crew = site.Team.Where(p => p.WorksOn(day)).ToList();
        if (crew.Count == 0)
        {
            return;
        }

        var weekStart = day.AddDays(-7);
        var days = Enumerable.Range(0, 6)
            .Select(weekStart.AddDays)
            .Where(d => d >= site.StartedOn)
            .Select(d => new LabourDayRequest
            {
                Date = d,
                Workers = Random.Chance(0.08) ? 0 : Math.Max(1, site.Workers + Random.Next(-2, 3)),
                DailyPay = site.DailyPay,
            })
            .ToList();
        await api.PutAsync(Random.Pick(crew).Token, $"/api/projects/{site.Id}/labour-weeks/{weekStart:yyyy-MM-dd}", new SubmitLabourWeekRequest
        {
            Days = days, Note = "LT weekly labour report",
        });
        world.Tally.Add("labour_weeks");
    }

    private static bool DueForStatement(SeedProject site, DateOnly day)
        => site.Unstated.Count >= 6 && (day.DayNumber - (site.LastStatementOn ?? site.StartedOn).DayNumber) >= 21;

    private async Task SendStatementAsync(SeedProject site, DateOnly day)
    {
        clock.MoveTo(day, 14);
        var admin = site.Owner.WorksOn(day) ? site.Owner : world.AnyAdmin("execution", day);
        if (Random.Chance(0.3))
        {
            var (materialId, cost) = Random.Pick(site.Unstated);
            await api.PutAsync(admin.Token, $"/api/projects/{site.Id}/materials/{materialId}/client-price", new SetMaterialPriceRequest
            {
                ClientPrice = decimal.Round(cost * (1 + site.Markup + 0.05m), 2),
            });
            world.Tally.Add("materials.priced");
        }

        var statement = await api.PostAsync(admin.Token, $"/api/projects/{site.Id}/statements", new CreateStatementRequest { Mode = StatementMode.Detail });
        await api.PostAsync(admin.Token, $"/api/statements/{statement.Id()}/deliveries", new RecordDeliveryRequest { Channel = DeliveryChannel.Download });
        site.Unstated.Clear();
        site.LastStatementOn = day;
        world.Tally.Add("statements");
    }
}
