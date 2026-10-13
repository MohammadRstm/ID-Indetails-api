using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Portal.Implementation;

public sealed class PortalContentViews(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPortalContentRepository content,
    IContentStore store,
    IClock clock,
    IOptions<PortalContentOptions> options) : IPortalContentViews
{
    private static readonly PortalSection[] Sections = [PortalSection.Presentation, PortalSection.Model, PortalSection.Drawing];

    public async Task<PortalContentProject> VisibleProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ClientPortalManage);
        var project = await content.GetProjectAsync(projectId, cancellationToken);
        return project is not null && project.DomainCode == DomainCodes.Design && actor.CanSee(project.DomainCode)
            ? project
            : throw new NotFoundException("Project not found.");
    }

    public async Task<PortalContentResponse> ContentAsync(PortalContentProject project, CancellationToken cancellationToken = default)
    {
        var items = await content.ItemsAsync(project.Id, cancellationToken);
        var settings = await content.SettingsAsync(project.Id, cancellationToken);
        var denial = PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status);
        var canEdit = store.IsAvailable && denial is null;
        var changes = PortalPublishRules.ChangesOf(
            items.Select(i => i.Copies).ToList(), settings.HeroFileId, settings.PublishedHeroFileId, settings.Summary, settings.PublishedSummary);

        var described = new List<PortalContentItemResponse>(items.Count);
        foreach (var item in items)
        {
            described.Add(await ItemAsync(item, settings.HeroFileId, cancellationToken));
        }

        return new PortalContentResponse
        {
            ProjectId = project.Id,
            StorageReady = store.IsAvailable,
            CanEdit = canEdit,
            ReadOnlyReason = denial?.Message,
            Usage = Usage(await content.UsageAsync(project.Id, cancellationToken)),
            AcceptedExtensions = PortalContentRules.AcceptedExtensions,
            Items = described,
            Revision = settings.Revision,
            Publication = new PortalPublicationResponse(
                settings.PublishedAt,
                settings.PublishedById is { } publisherId && settings.PublishedByName is { } name ? new PersonRefResponse(publisherId, name) : null),
            Changes = new PortalChangesResponse(
                changes.Added, changes.Updated, changes.Removed, changes.Reordered, changes.HeroChanged, changes.SummaryChanged),
            HasUnpublishedChanges = changes.Any,
            CanPublish = canEdit && changes.Any,
            CanDiscard = canEdit && changes.Any && settings.PublishedAt is not null,
            Hero = settings.HeroFileId is { } heroFileId ? await HeroAsync(heroFileId, cancellationToken) : null,
            Summary = settings.Summary,
        };
    }

    public async Task<PortalContentItemResponse> ItemAsync(PortalItemView item, long? heroFileId, CancellationToken cancellationToken = default)
    {
        var preview = PortalContentRules.PreviewOf(item.File.ContentType);
        var (previewUrl, previewExpiresAt) = await PreviewLinkAsync(item.File, preview, cancellationToken);
        var state = PortalPublishRules.StateOf(item.Copies);

        return new PortalContentItemResponse
        {
            Id = item.Id,
            Section = item.Section,
            Title = item.Title,
            SortOrder = item.SortOrder,
            IsDraft = state != PortalItemState.Unchanged,
            State = state,
            IsHero = heroFileId == item.File.Id,
            CanBeHero = state != PortalItemState.Removed && PortalPublishRules.CanBeHero(item.File.ContentType),
            File = new PortalContentFileResponse
            {
                Id = item.File.Id,
                Name = item.File.OriginalName,
                ContentType = item.File.ContentType,
                Preview = preview,
                OriginalBytes = item.File.OriginalSizeBytes,
                StoredBytes = item.File.StoredSizeBytes,
                UploadedAt = item.File.CreatedAt,
                UploadedBy = item.File.UploadedByName is null ? null : new PersonRefResponse(item.File.UploadedById, item.File.UploadedByName),
            },
            PreviewUrl = previewUrl,
            PreviewUrlExpiresAt = previewExpiresAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedByName is null ? null : new PersonRefResponse(item.UpdatedById, item.UpdatedByName),
        };
    }

    public async Task<PortalPresentationResponse> PresentationAsync(
        PortalContentProject project, bool published, CancellationToken cancellationToken = default)
    {
        var settings = await content.SettingsAsync(project.Id, cancellationToken);
        var isPublished = !published || settings.PublishedAt is not null;
        IReadOnlyList<PresentationRow> rows = isPublished ? await content.PresentationAsync(project.Id, published, cancellationToken) : [];
        var heroFileId = !isPublished ? null : published ? settings.PublishedHeroFileId : settings.HeroFileId;

        var sections = new List<PresentationSectionResponse>();
        foreach (var section in Sections)
        {
            var items = new List<PresentationItemResponse>();
            foreach (var row in rows.Where(r => r.Section == section))
            {
                var preview = PortalContentRules.PreviewOf(row.File.ContentType);
                var (url, expiresAt) = await PreviewLinkAsync(row.File, preview, cancellationToken);
                items.Add(new PresentationItemResponse
                {
                    Id = row.ItemId,
                    Title = row.Title,
                    Name = row.File.OriginalName,
                    ContentType = row.File.ContentType,
                    Preview = preview,
                    SizeBytes = row.File.OriginalSizeBytes,
                    PreviewUrl = url,
                    PreviewUrlExpiresAt = expiresAt,
                });
            }

            sections.Add(new PresentationSectionResponse(section, items));
        }

        PresentationHeroResponse? hero = null;
        if (heroFileId is { } fileId && store.IsAvailable && await content.FileViewAsync(fileId, cancellationToken) is { } heroFile)
        {
            var expiresAt = clock.UtcNow.AddMinutes(options.Value.PreviewLinkMinutes);
            var url = await store.GetDownloadUrlAsync(heroFile.StorageKey, heroFile.OriginalName, heroFile.ContentType, true, expiresAt, cancellationToken);
            hero = new PresentationHeroResponse(url, expiresAt, heroFile.OriginalName);
        }

        return new PortalPresentationResponse
        {
            ProjectId = project.Id,
            Title = project.Title,
            Status = project.Status,
            Summary = !isPublished ? null : published ? settings.PublishedSummary : settings.Summary,
            IsPublished = isPublished,
            PublishedAt = settings.PublishedAt,
            Hero = hero,
            Sections = sections,
        };
    }

    private async Task<PortalHeroResponse?> HeroAsync(long fileId, CancellationToken cancellationToken)
    {
        var file = await content.FileViewAsync(fileId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var (url, expiresAt) = await PreviewLinkAsync(file, PortalPreview.Image, cancellationToken);
        return new PortalHeroResponse(file.Id, file.OriginalName, url, expiresAt);
    }

    /// <summary>A short-lived inline link for images and videos; nothing for other files, or without a store.</summary>
    private async Task<(string? Url, DateTime? ExpiresAt)> PreviewLinkAsync(PortalFileView file, PortalPreview preview, CancellationToken cancellationToken)
    {
        if (!store.IsAvailable || preview is not (PortalPreview.Image or PortalPreview.Video))
        {
            return (null, null);
        }

        var expiresAt = clock.UtcNow.AddMinutes(options.Value.PreviewLinkMinutes);
        var url = await store.GetDownloadUrlAsync(file.StorageKey, file.OriginalName, file.ContentType, true, expiresAt, cancellationToken);
        return (url, expiresAt);
    }

    private static PortalUsageResponse Usage(PortalUsage usage) => new(usage.Files, usage.OriginalBytes, usage.StoredBytes);
}
