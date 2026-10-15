using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// The client's side of the presentation: only the published copy, only on a project the account has live access to.
/// Every refusal is the same 404, so nothing tells a client whether a project, a draft or another client's file exists.
/// </summary>
public sealed class PortalPresentationService(
    ICurrentPortalAccount currentAccount,
    IPortalContentRepository content,
    IPortalAccessRepository access,
    IPortalContentViews views,
    IContentStore store,
    IClock clock,
    IOptions<PortalContentOptions> options) : IPortalPresentationService
{
    private const string NotAvailable = "This project isn't available.";

    public async Task<PortalPresentationResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        // what was published until now is no longer news; taken before the page is read, so a publish meanwhile stays news
        var openedAt = clock.UtcNow;
        var presentation = await views.PresentationAsync(await OpenProjectAsync(projectId, cancellationToken), published: true, cancellationToken);
        await access.MarkOpenedAsync(currentAccount.Id, projectId, openedAt, cancellationToken);
        return presentation;
    }

    public async Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default)
    {
        await OpenProjectAsync(projectId, cancellationToken);
        var file = await content.PublishedFileAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(NotAvailable);
        if (!store.IsAvailable)
        {
            throw new ServiceUnavailableException(ErrorCodes.ContentStorageUnavailable, "Files can't be opened right now. Try again later.");
        }

        var showInline = inline && PortalContentRules.PreviewOf(file.ContentType) != PortalPreview.None;
        var expiresAt = clock.UtcNow.AddMinutes(options.Value.DownloadLinkMinutes);
        var url = await store.GetDownloadUrlAsync(file.StorageKey, file.OriginalName, file.ContentType, showInline, expiresAt, cancellationToken);
        return new PortalContentLinkResponse(url, expiresAt);
    }

    private async Task<PortalContentProject> OpenProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        if (!await content.ClientCanOpenAsync(currentAccount.Id, projectId, cancellationToken))
        {
            throw new NotFoundException(NotAvailable);
        }

        return await content.GetProjectAsync(projectId, cancellationToken) ?? throw new NotFoundException(NotAvailable);
    }
}
