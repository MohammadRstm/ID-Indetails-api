using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.Extensions.Caching.Memory;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>
/// The company working week, break and overtime multiplier, dated. Every hours and pay calculation
/// reads the policies, so they are cached and dropped whenever one changes.
/// </summary>
public sealed class WorkingTimeService(
    ICurrentUser currentUser,
    ICompanyRepository company,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IMemoryCache cache,
    IClock clock) : IWorkingTimeService
{
    private const string CacheKey = "company:working-time";

    public async Task<IReadOnlyList<WorkingTimePolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default)
        => (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await company.GetPoliciesAsync(cancellationToken);
        }))!;

    public async Task<WorkingTimeResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var policies = await GetPoliciesAsync(cancellationToken);
        var today = clock.CompanyToday;
        var current = policies.LastOrDefault(p => p.EffectiveFrom <= today) ?? policies[0];

        return new WorkingTimeResponse(Describe(current), policies.OrderByDescending(p => p.EffectiveFrom).Select(Describe).ToList());
    }

    public async Task<WorkingTimeResponse> SetAsync(SetWorkingTimeRequest request, CancellationToken cancellationToken = default)
    {
        var effectiveFrom = request.EffectiveFrom ?? clock.CompanyToday;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var policy = await company.GetPolicyForUpdateAsync(effectiveFrom, cancellationToken);
        WorkWeek.CheckEffectiveFrom(effectiveFrom, clock.CompanyToday, policy is not null).ThrowIfDenied();

        // An existing policy on that date hasn't started yet (checked above), so it is replaced.
        if (policy is null)
        {
            policy = new WorkingTimePolicy { EffectiveFrom = effectiveFrom, CreatedAt = clock.UtcNow, CreatedById = currentUser.Id };
            company.AddPolicy(policy);
        }

        policy.WorkDays = WorkWeek.ToMask(request.WorkDays);
        policy.WorkStartMinute = (short)request.StartMinute;
        policy.WorkEndMinute = (short)request.EndMinute;
        policy.BreakMinutes = (short)request.BreakMinutes;
        policy.OvertimeMultiplier = request.OvertimeMultiplier;

        activity.Add(new ActivityEvent
        {
            Kind = ActivityEventKinds.WorkingTimeChanged,
            OccurredAt = clock.UtcNow,
            ActorId = currentUser.Id,
            SubjectType = "working_time_policy",
            Payload = JsonSerializer.Serialize(new { effectiveFrom }),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache.Remove(CacheKey);

        return await GetAsync(cancellationToken);
    }

    public static WorkingTimePolicyResponse Describe(WorkingTimePolicy p)
    {
        var days = WorkWeek.FromMask(p.WorkDays);
        var perDay = WorkWeek.RequiredMinutes(p.WorkStartMinute, p.WorkEndMinute, p.BreakMinutes);
        return new WorkingTimePolicyResponse(
            p.Id, p.EffectiveFrom, days, p.WorkStartMinute, p.WorkEndMinute, p.BreakMinutes, p.OvertimeMultiplier, perDay, perDay * days.Count);
    }
}
