using ID_Indetails.api.Authorization;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Files.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
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
    IFileClaims fileClaims,
    IFileRepository files,
    IFileStorage storage,
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
        settings.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        settings.Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim();
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

    public async Task<CompanySettingsResponse> SetContactEmailAsync(CompanyContactEmailRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await company.GetSettingsAsync(forUpdate: true, cancellationToken);
        if (settings is null)
        {
            // like the logo, it may come first: the row starts with no name
            settings = new CompanySettings { TimeZone = options.Value.TimeZone };
            company.AddSettings(settings);
        }

        settings.ContactEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
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

    public Task<CompanySettingsResponse> SetProtonFilesAsync(CompanyProtonSwitchRequest request, CancellationToken cancellationToken = default)
        => SetProtonSwitchAsync(s => s.ProtonReadsFiles = request.Enabled, cancellationToken);

    public Task<CompanySettingsResponse> SetProtonSuggestionsAsync(CompanyProtonSwitchRequest request, CancellationToken cancellationToken = default)
        => SetProtonSwitchAsync(s => s.ProtonReplySuggestions = request.Enabled, cancellationToken);

    private async Task<CompanySettingsResponse> SetProtonSwitchAsync(Action<CompanySettings> change, CancellationToken cancellationToken)
    {
        var settings = await company.GetSettingsAsync(forUpdate: true, cancellationToken);
        if (settings is null)
        {
            settings = new CompanySettings { TimeZone = options.Value.TimeZone };
            company.AddSettings(settings);
        }

        change(settings);
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

    public Task<CompanySettingsResponse> SetLogoAsync(AttachFileRequest request, CancellationToken cancellationToken = default)
        => ChangeLogoAsync(request.FileId, cancellationToken);

    /// <summary>The old logo is attached to nothing any more, so the cleanup deletes it.</summary>
    public Task<CompanySettingsResponse> RemoveLogoAsync(CancellationToken cancellationToken = default)
        => ChangeLogoAsync(null, cancellationToken);

    public async Task<BrandingResponse> BrandingAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken);
        return new BrandingResponse(settings.IsSetUp ? settings.Name : null, settings.LogoFileId is not null, settings.LogoFileId);
    }

    public async Task<FileContent> OpenLogoAsync(CancellationToken cancellationToken = default)
    {
        var file = (await GetAsync(cancellationToken)).LogoFileId is { } id ? await files.GetAsync(id, cancellationToken) : null;
        return file is null
            ? throw new NotFoundException("The company has no logo.")
            : new FileContent(storage.OpenRead(file.StorageKey), file.MimeType, file.OriginalName);
    }

    private async Task<CompanySettingsResponse> ChangeLogoAsync(long? fileId, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var settings = await company.GetSettingsAsync(forUpdate: true, cancellationToken);
        if (settings is null)
        {
            // The logo may come first: the row starts with no name, so the settings still read as not set up.
            settings = new CompanySettings { TimeZone = options.Value.TimeZone };
            company.AddSettings(settings);
        }

        settings.LogoFileId = fileId is { } id ? (await fileClaims.ClaimAsync(id, FileKind.Image, cancellationToken)).Id : null;
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
        await transaction.CommitAsync(cancellationToken);
        cache.Remove(CacheKey);
        return Describe(settings);
    }

    private CompanySettingsResponse Describe(CompanySettings? settings)
    {
        var s = settings ?? new CompanySettings();
        var named = !string.IsNullOrWhiteSpace(s.Name);
        return new CompanySettingsResponse(
            named, named ? s.Name : options.Value.DefaultName, s.Address, s.Website, s.ContactEmail, s.LogoFileId, options.Value.TimeZone, s.WeekStart, s.Use24HourTime, s.DefaultCurrency, s.UpdatedAt,
            s.ProtonReadsFiles, s.ProtonReplySuggestions);
    }
}
