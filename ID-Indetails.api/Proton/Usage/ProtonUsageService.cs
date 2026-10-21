using System.Text.Json;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Usage;

/// <summary>
/// Proton's monthly limits and its usage ledger. People see their question allowance; only super admins see money
/// (the estimated provider cost and the budget). See <see cref="ProtonUsageRules"/> for how a month is limited.
/// </summary>
public sealed class ProtonUsageService(
    IProtonUsageRepository usage,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<ProtonOptions> options,
    ProtonBudgetHolds holds)
{
    private const int TopConversations = 10;

    public bool IsSwitchedOn => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public ProtonTokenPrices Prices
    {
        get
        {
            var prices = options.Value.Prices;
            return new ProtonTokenPrices(prices.InputPerMillion, prices.OutputPerMillion, prices.CacheReadPerMillion, prices.CacheWritePerMillion)
            {
                LongPromptAbove = prices.LongPromptAboveTokens,
                LongPrompt = prices.LongPromptAboveTokens > 0
                    ? new ProtonTokenPrices(prices.LongInputPerMillion, prices.LongOutputPerMillion, prices.LongCacheReadPerMillion, prices.LongCacheWritePerMillion)
                    : null,
            };
        }
    }

    public async Task<ProtonUsageResponse> MineAsync(long userId, CancellationToken cancellationToken = default)
    {
        var month = Month();
        var (from, to) = Bounds(month);
        var questions = ProtonUsageRules.Questions(
            options.Value.MonthlyQuestionsPerUser, await usage.QuestionsAsync(userId, from, to, cancellationToken));
        var budget = await BudgetPositionAsync(month, cancellationToken);
        var available = IsSwitchedOn && !questions.IsExhausted && !budget.IsExhausted;
        return new ProtonUsageResponse(
            month,
            available,
            (int)questions.Limit,
            (int)questions.Used,
            (int)questions.Remaining,
            questions.UsedPct,
            budget.IsExhausted,
            available && options.Value.Voice.IsOn,
            options.Value.Voice.IsOn ? options.Value.Voice.MaxSessionMinutes : 0);
    }

    /// <summary>Refuses the question when Proton is off, the person's allowance is used up, or the company's budget is.</summary>
    public async Task<ProtonUsageResponse> EnsureCanAskAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (!IsSwitchedOn)
        {
            throw new ServiceUnavailableException(ErrorCodes.ProtonUnavailable, "Proton isn't switched on.");
        }

        var mine = await MineAsync(userId, cancellationToken);
        if (mine.CompanyLimitReached)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonBudgetUsed, "Proton has used this month's budget. A super admin can top it up.");
        }

        if (mine.QuestionsRemaining == 0)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonAllowanceUsed, "You have used this month's Proton questions.");
        }

        return mine;
    }

    /// <summary>
    /// Holds this question's estimated cost on the budget until it is recorded. Refused when the budget, less what
    /// questions being answered already hold, can't cover it.
    /// </summary>
    public async Task<ProtonBudgetHold> HoldBudgetAsync(CancellationToken cancellationToken = default)
    {
        var amount = options.Value.QuestionHoldUsd;
        await holds.Deciding.WaitAsync(cancellationToken);
        try
        {
            var budget = await BudgetPositionAsync(Month(), cancellationToken);
            if (!ProtonUsageRules.CanHold(budget.Remaining, holds.Held, amount))
            {
                throw new BusinessRuleException(ErrorCodes.ProtonBudgetUsed, "Proton has used this month's budget. A super admin can top it up.");
            }

            return holds.Add(amount);
        }
        finally
        {
            holds.Deciding.Release();
        }
    }

    /// <summary>
    /// What is left of this month's budget, in USD, less what questions being answered hold: what a running voice
    /// session may still spend.
    /// </summary>
    public async Task<decimal> BudgetRemainingAsync(CancellationToken cancellationToken = default)
        => Math.Max(0, (await BudgetPositionAsync(Month(), cancellationToken)).Remaining - holds.Held);

    public async Task RecordAsync(ProtonUsage turn, CancellationToken cancellationToken = default)
    {
        usage.Add(turn);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProtonBudgetResponse> BudgetAsync(CancellationToken cancellationToken = default)
    {
        var month = Month();
        var (from, to) = Bounds(month);
        var position = await BudgetPositionAsync(month, cancellationToken);
        var topUps = await usage.TopUpsAsync(month, cancellationToken);
        var byUser = await usage.ByUserAsync(from, to, cancellationToken);
        var turns = (await usage.TurnsAsync(from, to, cancellationToken))
            .Select(t => new ProtonUsageTurn(t.UserId, t.Domain, t.ConversationId, clock.ToCompanyDate(t.OccurredAt), t.CostUsd, ToolCallsOf(t.Tools)))
            .ToList();
        var names = byUser.ToDictionary(u => u.UserId, u => u.Name);
        var voice = await usage.VoiceAsync(from, to, cancellationToken);
        return new ProtonBudgetResponse(
            month,
            options.Value.Model,
            options.Value.MonthlyBudgetUsd,
            topUps.Sum(t => t.AmountUsd),
            position.Limit,
            // to the same 4 places as the breakdowns: a month of Haiku questions can cost less than a cent
            Math.Round(position.Used, 4),
            Math.Round(position.Remaining, 4),
            position.UsedPct,
            byUser.Sum(u => u.Questions),
            topUps.Select(t => new ProtonTopUpResponse(t.Id, t.AmountUsd, t.Note, t.AddedBy, t.AddedAt)).ToList(),
            byUser.Select(u => new ProtonUserUsageResponse(u.UserId, u.Name, u.Questions, Math.Round(u.CostUsd, 4), u.Tokens)).ToList(),
            ProtonUsageBreakdown.ByDomain(turns).Select(d => new ProtonDomainUsageResponse(d.Domain, d.Questions, Math.Round(d.CostUsd, 4))).ToList(),
            ProtonUsageBreakdown.ByTool(turns)
                .Select(t => new ProtonToolUsageResponse(t.Tool, t.Calls, t.TotalMs / t.Calls, t.Questions, Math.Round(t.QuestionCostUsd, 4)))
                .ToList(),
            ProtonUsageBreakdown.ByDay(turns).Select(d => new ProtonDayUsageResponse(d.Day, d.Questions, Math.Round(d.CostUsd, 4))).ToList(),
            ProtonUsageBreakdown.MostExpensiveConversations(turns, TopConversations)
                .Select(c => new ProtonConversationUsageResponse(
                    c.ConversationId, c.UserId, names.GetValueOrDefault(c.UserId, "Unknown"), c.Questions, c.ToolCalls, Math.Round(c.CostUsd, 4)))
                .ToList(),
            new ProtonVoiceUsageResponse(
                options.Value.Voice.Model, voice.Sessions, Math.Round(voice.Seconds / 60m, 1), Math.Round(voice.CostUsd, 4), voice.Estimated));
    }

    /// <summary>Adds to this month's budget. Top-ups are kept, never edited, so the month's budget can be explained.</summary>
    public async Task<ProtonBudgetResponse> TopUpAsync(long addedById, TopUpProtonBudgetRequest request, CancellationToken cancellationToken = default)
    {
        usage.Add(new ProtonBudgetTopUp
        {
            Month = Month(),
            AmountUsd = request.AmountUsd,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            AddedById = addedById,
            AddedAt = clock.UtcNow,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await BudgetAsync(cancellationToken);
    }

    private async Task<ProtonAllowance> BudgetPositionAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var (from, to) = Bounds(month);
        return ProtonUsageRules.Budget(
            options.Value.MonthlyBudgetUsd,
            await usage.TopUpTotalAsync(month, cancellationToken),
            await usage.SpentAsync(from, to, cancellationToken));
    }

    /// <summary>The tool calls of a recorded turn (see ProtonTurn.Record): name and duration only.</summary>
    private static IReadOnlyList<ProtonToolCallUsage> ToolCallsOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Select(c => new ProtonToolCallUsage(c.GetProperty("tool").GetString() ?? "", c.TryGetProperty("ms", out var ms) ? ms.GetInt32() : 0))
            .ToList();
    }

    private DateOnly Month() => ProtonUsageRules.MonthOf(clock.CompanyToday);

    private (DateTime From, DateTime To) Bounds(DateOnly month)
        => (clock.CompanyDayStartUtc(month), clock.CompanyDayStartUtc(month.AddMonths(1)));
}
