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
    IOptions<ProtonOptions> options)
{
    public bool IsSwitchedOn => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public ProtonTokenPrices Prices => new(
        options.Value.Prices.InputPerMillion,
        options.Value.Prices.OutputPerMillion,
        options.Value.Prices.CacheReadPerMillion,
        options.Value.Prices.CacheWritePerMillion);

    public async Task<ProtonUsageResponse> MineAsync(long userId, CancellationToken cancellationToken = default)
    {
        var month = Month();
        var (from, to) = Bounds(month);
        var questions = ProtonUsageRules.Questions(
            options.Value.MonthlyQuestionsPerUser, await usage.QuestionsAsync(userId, from, to, cancellationToken));
        var budget = await BudgetPositionAsync(month, cancellationToken);
        return new ProtonUsageResponse(
            month,
            IsSwitchedOn && !questions.IsExhausted && !budget.IsExhausted,
            (int)questions.Limit,
            (int)questions.Used,
            (int)questions.Remaining,
            questions.UsedPct,
            budget.IsExhausted);
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
        return new ProtonBudgetResponse(
            month,
            options.Value.Model,
            options.Value.MonthlyBudgetUsd,
            topUps.Sum(t => t.AmountUsd),
            position.Limit,
            Math.Round(position.Used, 2),
            Math.Round(position.Remaining, 2),
            position.UsedPct,
            byUser.Sum(u => u.Questions),
            topUps.Select(t => new ProtonTopUpResponse(t.Id, t.AmountUsd, t.Note, t.AddedBy, t.AddedAt)).ToList(),
            byUser.Select(u => new ProtonUserUsageResponse(u.UserId, u.Name, u.Questions, Math.Round(u.CostUsd, 4), u.Tokens)).ToList());
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

    private DateOnly Month() => ProtonUsageRules.MonthOf(clock.CompanyToday);

    private (DateTime From, DateTime To) Bounds(DateOnly month)
        => (clock.CompanyDayStartUtc(month), clock.CompanyDayStartUtc(month.AddMonths(1)));
}
