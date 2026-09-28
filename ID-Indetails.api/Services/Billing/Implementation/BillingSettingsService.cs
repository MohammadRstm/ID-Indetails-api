using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>Each domain has its own billing settings. Settings of a domain the caller can't see read as not found.</summary>
public sealed class BillingSettingsService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IBillingSettingsRepository settings,
    IExchangeRateRepository exchangeRates,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IBillingSettingsService
{
    public async Task<BillingSettingsResponse> GetAsync(string domainCode, CancellationToken cancellationToken = default)
    {
        await EnsureVisibleAsync(domainCode, cancellationToken);
        var row = await settings.GetAsync(domainCode, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.BillingSettingsMissing, "Billing settings have not been set up for this domain yet.");

        return ToResponse(row);
    }

    public async Task<BillingSettingsResponse> SaveAsync(
        string domainCode,
        SaveBillingSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureVisibleAsync(domainCode, cancellationToken);

        // Invoice numbers are unique across the company, so two domains can't share a prefix.
        if (await settings.PrefixUsedElsewhereAsync(request.InvoicePrefix, domainCode, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.InvoicePrefixTaken, "The other domain already uses this invoice prefix.");
        }

        var row = await settings.GetForUpdateAsync(domainCode, cancellationToken);
        if (row is null)
        {
            row = new BillingSettings { DomainCode = domainCode };
            settings.Add(row);
        }

        row.InvoicePrefix = request.InvoicePrefix;
        row.DefaultTermsDays = request.DefaultTermsDays;
        row.BankAccount = Clean(request.BankAccount);
        row.BankName = Clean(request.BankName);
        row.Iban = Clean(request.Iban)?.Replace(" ", string.Empty).ToUpperInvariant();
        row.Swift = Clean(request.Swift)?.ToUpperInvariant();
        row.TermsText = Clean(request.TermsText);
        row.Footer = Clean(request.Footer);
        row.UpdatedAt = clock.UtcNow;
        row.UpdatedById = actorId;

        activity.Add(new ActivityEvent
        {
            DomainCode = domainCode,
            Kind = ActivityEventKinds.BillingSettingsUpdated,
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            SubjectType = "billing_settings",
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(row);
    }

    public async Task<IReadOnlyList<ExchangeRateResponse>> ListExchangeRatesAsync(
        ExchangeRateListQuery query,
        CancellationToken cancellationToken = default)
        => (await exchangeRates.ListAsync(query.Currency?.ToUpperInvariant(), cancellationToken))
            .Select(r => new ExchangeRateResponse(r.Id, r.Currency, r.RatePerUsd, r.EffectiveFrom, r.CreatedAt))
            .ToList();

    /// <summary>The caller's id, once the domain is known to be one they can see.</summary>
    private async Task<long> EnsureVisibleAsync(string domainCode, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!DomainCodes.All.Contains(domainCode) || !actor.CanSee(domainCode))
        {
            throw new NotFoundException("Domain not found.");
        }

        return actor.UserId;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BillingSettingsResponse ToResponse(BillingSettings s) => new(
        s.DomainCode, s.InvoicePrefix, s.DefaultTermsDays, s.BankAccount, s.BankName, s.Iban, s.Swift, s.TermsText, s.Footer, s.UpdatedAt);
}
