using ID_Indetails.api.Business.Clients;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ClientGrowthTests
{
    [Fact]
    public void Growth_counts_the_clients_on_the_books_at_each_month_end_and_names_the_ones_added()
    {
        var clients = new[]
        {
            new ClientGrowth.Added(1, "Old client", new DateOnly(2024, 5, 2)),
            new ClientGrowth.Added(2, "Spring", new DateOnly(2026, 3, 30)),
            new ClientGrowth.Added(3, "Autumn", new DateOnly(2026, 9, 1)),
            new ClientGrowth.Added(4, "Autumn too", new DateOnly(2026, 9, 18)),
        };

        var months = ClientGrowth.Of(clients, new DateOnly(2026, 9, 30));

        months.Count.ShouldBe(13);
        months[0].Start.ShouldBe(new DateOnly(2025, 9, 1));
        months[0].Total.ShouldBe(1);
        months.Single(m => m.Start == new DateOnly(2026, 3, 1)).AddedInMonth.Select(c => c.Id).ShouldBe([2L]);
        months[^1].Start.ShouldBe(new DateOnly(2026, 9, 1));
        months[^1].Total.ShouldBe(4);
        months[^1].AddedInMonth.Select(c => c.Name).ShouldBe(["Autumn", "Autumn too"]);
    }
}
