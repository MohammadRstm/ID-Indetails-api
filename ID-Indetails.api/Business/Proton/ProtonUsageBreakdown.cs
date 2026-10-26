namespace ID_Indetails.api.Business.Proton;

public sealed record ProtonToolCallUsage(string Tool, int Ms);

/// <param name="Domain">The person's domain (null for company-wide roles).</param>
/// <param name="Day">The company day it was asked.</param>
public sealed record ProtonUsageTurn(long UserId, string? Domain, Guid ConversationId, DateOnly Day, decimal CostUsd, IReadOnlyList<ProtonToolCallUsage> Tools);

public sealed record ProtonDomainUsage(string? Domain, int Questions, decimal CostUsd);

/// <param name="Questions">Questions that used the tool at least once.</param>
/// <param name="QuestionCostUsd">What those questions cost in all: a tool's own share of a question can't be told apart.</param>
public sealed record ProtonToolUsage(string Tool, int Calls, int TotalMs, int Questions, decimal QuestionCostUsd);

public sealed record ProtonDayUsage(DateOnly Day, int Questions, decimal CostUsd);

public sealed record ProtonConversationUsage(Guid ConversationId, long UserId, int Questions, int ToolCalls, decimal CostUsd);

/// <summary>Where Proton's budget goes, for super admins: by domain, tool, day and conversation, from the usage ledger.</summary>
public static class ProtonUsageBreakdown
{
    public static IReadOnlyList<ProtonDomainUsage> ByDomain(IEnumerable<ProtonUsageTurn> turns)
        => turns.GroupBy(t => t.Domain)
            .Select(g => new ProtonDomainUsage(g.Key, g.Count(), g.Sum(t => t.CostUsd)))
            .OrderByDescending(d => d.CostUsd)
            .ToList();

    public static IReadOnlyList<ProtonToolUsage> ByTool(IEnumerable<ProtonUsageTurn> turns)
        => turns.SelectMany(t => t.Tools.Select(c => (Turn: t, Call: c)))
            .GroupBy(x => x.Call.Tool)
            .Select(g =>
            {
                var questions = g.Select(x => x.Turn).Distinct().ToList();
                return new ProtonToolUsage(g.Key, g.Count(), g.Sum(x => x.Call.Ms), questions.Count, questions.Sum(t => t.CostUsd));
            })
            .OrderByDescending(t => t.Calls)
            .ThenBy(t => t.Tool, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<ProtonDayUsage> ByDay(IEnumerable<ProtonUsageTurn> turns)
        => turns.GroupBy(t => t.Day)
            .Select(g => new ProtonDayUsage(g.Key, g.Count(), g.Sum(t => t.CostUsd)))
            .OrderBy(d => d.Day)
            .ToList();

    public static IReadOnlyList<ProtonConversationUsage> MostExpensiveConversations(IEnumerable<ProtonUsageTurn> turns, int count)
        // reply suggestions belong to no conversation
        => turns.Where(t => t.ConversationId != Guid.Empty).GroupBy(t => t.ConversationId)
            .Select(g => new ProtonConversationUsage(g.Key, g.First().UserId, g.Count(), g.Sum(t => t.Tools.Count), g.Sum(t => t.CostUsd)))
            .OrderByDescending(c => c.CostUsd)
            .Take(count)
            .ToList();
}
