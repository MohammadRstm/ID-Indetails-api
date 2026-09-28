using ID_Indetails.api.Authorization;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>Read by every page, so it is cached and dropped when saved.</summary>
public sealed class CompanySettingsService(
    ICurrentUser currentUser,
    ICompanyRepository company,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IMemoryCache cache,
    IOptions<CompanyOptions> options,
    IClock clock) : ICompanySettingsService
{
    private const string CacheKey = "company:settings";

    public async Task<CompanySettingsResponse> GetAsync(CancellationToken cancellationToken = default)
        => (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return Describe(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        }))!;

    public async Task<CompanySettingsResponse> UpdateAsync(UpdateCompanySettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await company.GetSettingsAsync(forUpdate: true, cancellationToken);
        if (settings is null)
        {
            settings = new CompanySettings();
            company.AddSettings(settings);
        }

        settings.Name = request.Name.Trim();
        settings.TimeZone = options.Value.TimeZone;
        settings.WeekStart = request.WeekStart;
        settings.Use24HourTime = request.Use24HourTime;
        settings.DefaultCurrency = request.DefaultCurrency.ToUpperInvariant();
        settings.UpdatedAt = clock.UtcNow;
        settings.UpdatedById = currentUser.Id;

        activity.Add(new ActivityEvent
        {
            Kind = ActivityEventKinds.CompanySettingsUpdated,
            OccurredAt = clock.UtcNow,
            ActorId = currentUser.Id,
            SubjectType = "company_settings",
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);

        return Describe(settings);
    }

    private CompanySettingsResponse Describe(CompanySettings? settings)
    {
        var s = settings ?? new CompanySettings();
        return new CompanySettingsResponse(
            settings is not null, s.Name, options.Value.TimeZone, s.WeekStart, s.Use24HourTime, s.DefaultCurrency, s.UpdatedAt);
    }
}
