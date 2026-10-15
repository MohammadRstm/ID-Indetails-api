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
    IProjectFileCommentRepository comments,
    IContentStore store,
    IClock clock,
    IOptions<PortalContentOptions> options) : IPortalContentViews
{
    /// <summary>What anything needing the content store answers while none is configured (503).</summary>
    public const string StoreMissing = "Client portal file storage isn't set up yet. Ask your administrator to configure it.";

    private static readonly PortalSection[] Sections = [PortalSection.Presentation, PortalSection.Model, PortalSection.Drawing];

    public async Task<PortalContentProject> VisibleProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await AccessAsync(cancellationToken)).Require(PermissionCodes.ClientPortalManage, PermissionCodes.ClientAccessManage);
        return await DesignProjectAsync(projectId, actor, cancellationToken);
    }

    public async Task<PortalContentProject> ManagedProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await AccessAsync(cancellationToken)).Require(PermissionCodes.ClientPortalManage);
        return await DesignProjectAsync(projectId, actor, cancellationToken);
    }

    public async Task<ProjectFilesAccess> FilesAccessAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await AccessAsync(cancellationToken)).Require(PermissionCodes.ProjectView);
        var project = await DesignProjectAsync(projectId, actor, cancellationToken);
        return new ProjectFilesAccess(
            project, actor.UserId, actor.Has(PermissionCodes.ProjectFilesManage), actor.Has(PermissionCodes.ClientPortalManage));
    }

    public async Task<PortalContentResponse> ContentAsync(PortalContentProject project, CancellationToken cancellationToken = default)
    {
        var canManage = (await AccessAsync(cancellationToken)).Has(PermissionCodes.ClientPortalManage);
        var items = await content.ItemsAsync(project.Id, cancellationToken);
        var settings = await content.SettingsAsync(project.Id, cancellationToken);
        var denial = PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status);
        var canEdit = canManage && store.IsAvailable && denial is null;
        var changes = PortalPublishRules.ChangesOf(
            items.Select(i => i.Copies).ToList(), settings.HeroFileId, settings.PublishedHeroFileId, settings.Summary, settings.PublishedSummary);

        // the client portal shows the client-relevant files only; the others are the team's
        var described = new List<PortalContentItemResponse>();
        foreach (var item in items)
        {
            if (PortalPublishRules.StateOf(item.Copies) is { } state)
            {
                described.Add(await ItemAsync(item, state, settings.HeroFileId, cancellationToken));
            }
        }

        return new PortalContentResponse
        {
            ProjectId = project.Id,
            StorageReady = store.IsAvailable,
            CanManage = canManage,
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

    public async Task<ProjectFilesResponse> FilesAsync(ProjectFilesAccess access, CancellationToken cancellationToken = default)
    {
        var project = access.Project;
        var items = await content.ItemsAsync(project.Id, cancellationToken);
        var heroFileId = (await content.SettingsAsync(project.Id, cancellationToken)).HeroFileId;
        var counts = await comments.CountsAsync(project.Id, cancellationToken);
        var denial = ProjectFileRules.CheckEditable(project.Status);

        var described = new List<ProjectFileResponse>(items.Count);
        foreach (var item in items)
        {
            described.Add(await FileAsync(access, item, heroFileId, counts.GetValueOrDefault(item.Id) ?? ProjectFileCommentCounts.None, cancellationToken));
        }

        return new ProjectFilesResponse
        {
            ProjectId = project.Id,
            StorageReady = store.IsAvailable,
            CanEdit = store.IsAvailable && denial is null,
            ReadOnlyReason = denial?.Message,
            CanManage = access.ManagesFiles,
            CanChooseForClient = ProjectFileRules.CheckClientPortal(access) is null,
            IsClientProject = PortalContentRules.CheckEligible(project.BillingType, project.ClientId) is null,
            Usage = Usage(await content.UsageAsync(project.Id, cancellationToken)),
            AcceptedExtensions = PortalContentRules.AcceptedExtensions,
            Items = described,
        };
    }

    public async Task<ProjectFileResponse> FileAsync(ProjectFilesAccess access, long itemId, CancellationToken cancellationToken = default)
    {
        var item = await content.ItemViewAsync(access.Project.Id, itemId, cancellationToken) ?? throw new NotFoundException("File not found.");
        var heroFileId = (await content.SettingsAsync(access.Project.Id, cancellationToken)).HeroFileId;
        var counts = (await comments.CountsAsync(access.Project.Id, cancellationToken)).GetValueOrDefault(itemId) ?? ProjectFileCommentCounts.None;
        return await FileAsync(access, item, heroFileId, counts, cancellationToken);
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

    public async Task<PortalContentLinkResponse> LinkAsync(PortalFileView file, bool inline, CancellationToken cancellationToken = default)
    {
        if (!store.IsAvailable)
        {
            throw new ServiceUnavailableException(ErrorCodes.ContentStorageUnavailable, StoreMissing);
        }

        var showInline = inline && PortalContentRules.PreviewOf(file.ContentType) != PortalPreview.None;
        var expiresAt = clock.UtcNow.AddMinutes(options.Value.DownloadLinkMinutes);
        var url = await store.GetDownloadUrlAsync(file.StorageKey, file.OriginalName, file.ContentType, showInline, expiresAt, cancellationToken);
        return new PortalContentLinkResponse(url, expiresAt);
    }

    private Task<UserAccess> AccessAsync(CancellationToken cancellationToken) => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>The project, when it is a Design project the caller can see; anything else reads as not found.</summary>
    private async Task<PortalContentProject> DesignProjectAsync(long projectId, UserAccess actor, CancellationToken cancellationToken)
    {
        var project = await content.GetProjectAsync(projectId, cancellationToken);
        return project is not null && project.DomainCode == DomainCodes.Design && actor.CanSee(project.DomainCode)
            ? project
            : throw new NotFoundException("Project not found.");
    }

    private async Task<PortalContentItemResponse> ItemAsync(PortalItemView item, PortalItemState state, long? heroFileId, CancellationToken cancellationToken)
    {
        var preview = PortalContentRules.PreviewOf(item.File.ContentType);
        var (previewUrl, previewExpiresAt) = await PreviewLinkAsync(item.File, preview, cancellationToken);

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
            File = FileResponse(item.File, preview),
            PreviewUrl = previewUrl,
            PreviewUrlExpiresAt = previewExpiresAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedByName is null ? null : new PersonRefResponse(item.UpdatedById, item.UpdatedByName),
            UploadedBy = Uploader(item.File),
        };
    }

    private async Task<ProjectFileResponse> FileAsync(
        ProjectFilesAccess access, PortalItemView item, long? heroFileId, ProjectFileCommentCounts counts, CancellationToken cancellationToken)
    {
        var preview = PortalContentRules.PreviewOf(item.File.ContentType);
        var (previewUrl, previewExpiresAt) = await PreviewLinkAsync(item.File, preview, cancellationToken);
        var removed = item.RemovedAt is not null;

        return new ProjectFileResponse
        {
            Id = item.Id,
            Section = item.Section,
            Title = item.Title,
            SortOrder = item.SortOrder,
            ForClient = item.ForClient,
            Removed = removed,
            IsHero = heroFileId == item.File.Id,
            CanBeHero = !removed && PortalPublishRules.CanBeHero(item.File.ContentType),
            File = FileResponse(item.File, preview),
            PreviewUrl = previewUrl,
            PreviewUrlExpiresAt = previewExpiresAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedByName is null ? null : new PersonRefResponse(item.UpdatedById, item.UpdatedByName),
            UploadedBy = Uploader(item.File),
            CanEdit = ProjectFileRules.CheckEditable(access.Project.Status) is null && ProjectFileRules.CanEdit(access, item.File.UploadedById),
            Comments = new ProjectFileCommentCountsResponse(counts.Total, counts.Open),
            ClientState = PortalPublishRules.StateOf(item.Copies),
        };
    }

    private static PortalContentFileResponse FileResponse(PortalFileView file, PortalPreview preview)
        => new()
        {
            Id = file.Id,
            Name = file.OriginalName,
            ContentType = file.ContentType,
            Preview = preview,
            OriginalBytes = file.OriginalSizeBytes,
            StoredBytes = file.StoredSizeBytes,
            UploadedAt = file.CreatedAt,
            UploadedBy = Uploader(file),
        };

    private static PersonRefResponse? Uploader(PortalFileView file)
        => file.UploadedByName is null ? null : new PersonRefResponse(file.UploadedById, file.UploadedByName);

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
