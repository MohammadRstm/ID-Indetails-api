using ID_Indetails.api.Business.Proton;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonUsageBreakdownTests
{
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static readonly ProtonUsageTurn[] Turns =
    [
        new(1, "design", First, Monday, 0.02m, [new("find", 40), new("review_project", 900)]),
        new(1, "design", First, Monday, 0.05m, [new("compare_work", 300), new("compare_work", 200)]),
        new(2, null, Second, Monday.AddDays(1), 0.10m, [new("get_company_summary", 2_000)]),
        new(2, null, Second, Monday.AddDays(1), 0.01m, []),
    ];

    [Fact]
    public void Spending_is_split_by_domain_and_day()
    {
        ProtonUsageBreakdown.ByDomain(Turns).ShouldBe([new ProtonDomainUsage(null, 2, 0.11m), new ProtonDomainUsage("design", 2, 0.07m)]);
        ProtonUsageBreakdown.ByDay(Turns).ShouldBe([new ProtonDayUsage(Monday, 2, 0.07m), new ProtonDayUsage(Monday.AddDays(1), 2, 0.11m)]);
    }

    [Fact]
    public void A_tool_counts_its_calls_time_and_the_questions_it_was_part_of()
    {
        var compare = ProtonUsageBreakdown.ByTool(Turns).First();

        compare.ShouldBe(new ProtonToolUsage("compare_work", 2, 500, 1, 0.05m));
        ProtonUsageBreakdown.ByTool(Turns).Select(t => t.Tool).ShouldNotContain("propose_create_project");
    }

    [Fact]
    public void The_most_expensive_conversations_come_first()
    {
        var conversations = ProtonUsageBreakdown.MostExpensiveConversations(Turns, 1);

        conversations.ShouldBe([new ProtonConversationUsage(Second, 2, 2, 1, 0.11m)]);
    }
}
