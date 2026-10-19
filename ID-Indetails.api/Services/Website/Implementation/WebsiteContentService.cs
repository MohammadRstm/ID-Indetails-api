using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Website.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Website.Abstraction;
using Microsoft.Extensions.Options;
using ID_Indetails.api.Configuration.Options;

namespace ID_Indetails.api.Services.Website.Implementation;

/// <summary>
/// The website's services, locations and social links (three ordered lists edited the same way) and its favicon. The
/// company's contact email lives in the company settings (<c>PUT /api/company/contact-email</c>).
/// </summary>
public sealed class WebsiteContentService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IWebsiteRepository website,
    ICompanyRepository company,
    IFileClaims fileClaims,
    WebsitePlaceResolver places,
    IUnitOfWork unitOfWork,
    IOptions<CompanyOptions> companyOptions) : IWebsiteContentService
{
    public async Task<WebsiteContentResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var services = await website.ListAsync<WebsiteOffering>(cancellationToken: cancellationToken);
        var locations = await website.ListAsync<WebsiteLocation>(cancellationToken: cancellationToken);
        var links = await website.ListAsync<WebsiteSocialLink>(cancellationToken: cancellationToken);
        var settings = await company.GetSettingsAsync(cancellationToken: cancellationToken);
        var canManage = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Has(PermissionCodes.WebsiteManage);
        return new WebsiteContentResponse(
            services.Select(s => new WebsiteServiceResponse(s.Id, s.Title, s.Description)).ToList(),
            locations.Select(l => new WebsiteLocationResponse(l.Id, l.City, l.Country, l.Latitude, l.Longitude, l.PositionApproximate, l.Address, l.Phone)).ToList(),
            links.Select(l => new WebsiteSocialLinkResponse(l.Id, l.Label, l.Url)).ToList(),
            settings?.ContactEmail,
            settings?.WebsiteFaviconFileId,
            canManage);
    }

    public Task<WebsiteContentResponse> AddServiceAsync(WebsiteServiceRequest request, CancellationToken cancellationToken = default)
        => AddAsync(Set(new WebsiteOffering(), request), cancellationToken);

    public Task<WebsiteContentResponse> UpdateServiceAsync(long id, WebsiteServiceRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync<WebsiteOffering>(id, "Service", s => Set(s, request), cancellationToken);

    public Task<WebsiteContentResponse> DeleteServiceAsync(long id, CancellationToken cancellationToken = default)
        => DeleteAsync<WebsiteOffering>(id, "Service", cancellationToken);

    public Task<WebsiteContentResponse> OrderServicesAsync(WebsiteOrderRequest request, CancellationToken cancellationToken = default)
        => OrderAsync<WebsiteOffering>(request, "services", cancellationToken);

    public async Task<WebsiteContentResponse> AddLocationAsync(WebsiteLocationRequest request, CancellationToken cancellationToken = default)
        => await AddAsync(await PlaceAsync(new WebsiteLocation(), request, cancellationToken), cancellationToken);

    public async Task<WebsiteContentResponse> UpdateLocationAsync(long id, WebsiteLocationRequest request, CancellationToken cancellationToken = default)
    {
        var location = await website.FindAsync<WebsiteLocation>(id, cancellationToken) ?? throw new NotFoundException("Location not found.");
        await PlaceAsync(location, request, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public Task<WebsiteContentResponse> DeleteLocationAsync(long id, CancellationToken cancellationToken = default)
        => DeleteAsync<WebsiteLocation>(id, "Location", cancellationToken);

    public Task<WebsiteContentResponse> OrderLocationsAsync(WebsiteOrderRequest request, CancellationToken cancellationToken = default)
        => OrderAsync<WebsiteLocation>(request, "locations", cancellationToken);

    public Task<WebsiteContentResponse> AddSocialLinkAsync(WebsiteSocialLinkRequest request, CancellationToken cancellationToken = default)
        => AddAsync(Set(new WebsiteSocialLink(), request), cancellationToken);

    public Task<WebsiteContentResponse> UpdateSocialLinkAsync(long id, WebsiteSocialLinkRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync<WebsiteSocialLink>(id, "Link", l => Set(l, request), cancellationToken);

    public Task<WebsiteContentResponse> DeleteSocialLinkAsync(long id, CancellationToken cancellationToken = default)
        => DeleteAsync<WebsiteSocialLink>(id, "Link", cancellationToken);

    public Task<WebsiteContentResponse> OrderSocialLinksAsync(WebsiteOrderRequest request, CancellationToken cancellationToken = default)
        => OrderAsync<WebsiteSocialLink>(request, "links", cancellationToken);

    public Task<WebsiteContentResponse> SetFaviconAsync(AttachFileRequest request, CancellationToken cancellationToken = default)
        => ChangeFaviconAsync(request.FileId, cancellationToken);

    /// <summary>The old favicon is attached to nothing any more, so the file cleanup deletes it.</summary>
    public Task<WebsiteContentResponse> RemoveFaviconAsync(CancellationToken cancellationToken = default)
        => ChangeFaviconAsync(null, cancellationToken);

    private async Task<WebsiteContentResponse> ChangeFaviconAsync(long? fileId, CancellationToken cancellationToken)
    {
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await company.GetSettingsAsync(forUpdate: true, cancellationToken);
            if (settings is null)
            {
                // like the logo, it may come first: the row starts with no name
                settings = new CompanySettings { TimeZone = companyOptions.Value.TimeZone };
                company.AddSettings(settings);
            }

            settings.WebsiteFaviconFileId = fileId is { } id ? (await fileClaims.ClaimAsync(id, FileKind.Image, cancellationToken)).Id : null;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    private async Task<WebsiteContentResponse> AddAsync<T>(T row, CancellationToken cancellationToken) where T : class, IWebsiteListItem
    {
        row.Position = await website.NextPositionAsync<T>(cancellationToken);
        website.Add(row);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private async Task<WebsiteContentResponse> ChangeAsync<T>(long id, string what, Action<T> change, CancellationToken cancellationToken)
        where T : class, IWebsiteListItem
    {
        change(await website.FindAsync<T>(id, cancellationToken) ?? throw new NotFoundException($"{what} not found."));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private async Task<WebsiteContentResponse> DeleteAsync<T>(long id, string what, CancellationToken cancellationToken)
        where T : class, IWebsiteListItem
    {
        website.Remove(await website.FindAsync<T>(id, cancellationToken) ?? throw new NotFoundException($"{what} not found."));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private async Task<WebsiteContentResponse> OrderAsync<T>(WebsiteOrderRequest request, string what, CancellationToken cancellationToken)
        where T : class, IWebsiteListItem
    {
        var rows = await website.ListAsync<T>(track: true, cancellationToken);
        if (!WebsiteRules.IsWholeOrder(rows.Select(r => r.Id), request.Ids))
        {
            throw new ConflictException(ErrorCodes.WebsiteOrderStale, $"The {what} changed since you looked: one was added or removed. Your new order wasn't saved.");
        }

        var byId = rows.ToDictionary(r => r.Id);
        for (var index = 0; index < request.Ids.Count; index++)
        {
            byId[request.Ids[index]].Position = index + 1;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private static WebsiteOffering Set(WebsiteOffering service, WebsiteServiceRequest request)
    {
        service.Title = request.Title.Trim();
        service.Description = request.Description.Trim();
        return service;
    }

    /// <summary>
    /// The office's details and place (<see cref="WebsitePlaceResolver"/>). An office is public as soon as it's saved,
    /// so a place that can't be found is refused rather than kept half-done.
    /// </summary>
    private async Task<WebsiteLocation> PlaceAsync(WebsiteLocation location, WebsiteLocationRequest request, CancellationToken cancellationToken)
    {
        long? self = location.Id == 0 ? null : location.Id;
        var stored = self is null ? null : new StoredPlace(location.City, location.Country, location.Latitude, location.Longitude, location.PositionApproximate);
        var place = await places.ResolveAsync(request.City, request.Country, request.Latitude, request.Longitude, stored, new PlaceOwner(null, self), cancellationToken);
        if (place.Problem is not null || place.City is null || place.Country is null || place.Latitude is null || place.Longitude is null)
        {
            throw new BusinessRuleException(ErrorCodes.WebsitePlaceNotFound, place.Problem ?? "Give the office's coordinates, or its city and country.");
        }

        location.City = place.City;
        location.Country = place.Country;
        location.Latitude = place.Latitude.Value;
        location.Longitude = place.Longitude.Value;
        location.PositionApproximate = place.Approximate;
        location.Address = request.Address.Trim();
        location.Phone = request.Phone.Trim();
        return location;
    }

    private static WebsiteSocialLink Set(WebsiteSocialLink link, WebsiteSocialLinkRequest request)
    {
        link.Label = request.Label.Trim();
        link.Url = request.Url.Trim();
        return link;
    }
}
