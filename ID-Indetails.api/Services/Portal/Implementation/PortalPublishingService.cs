using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// The draft and the published copy of a project's client portal. Every change takes the project's content lock and
/// moves the revision on. Publishing copies the whole draft (items, order, hero, introduction) into the published
/// columns in one transaction: clients see the old version or the new one, never a mix, and a publish naming an older
/// revision than the current one is refused rather than publishing what the person didn't see.
/// </summary>
public sealed class PortalPublishingService(
    ICurrentUser currentUser,
    IPortalContentViews views,
    IPortalContentRepository content,
    IPortalContentMaintenance maintenance,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IPortalPublishingService
{
    private const string ItemNotFound = "File not found.";

    public Task<PortalContentResponse> ReorderAsync(
        long projectId, PortalSection section, PortalOrderRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, (settings, items, _) =>
        {
            var current = items.Where(i => i.RemovedAt is null && i.Section == section).ToDictionary(i => i.Id);
            if (!PortalPublishRules.IsWholeOrder(current.Keys, request.ItemIds))
            {
                throw new ConflictException(
                    ErrorCodes.PortalOrderStale,
                    "This section changed since you looked: a file was added, removed or moved. Your new order wasn't saved.");
            }

            for (var index = 0; index < request.ItemIds.Count; index++)
            {
                current[request.ItemIds[index]].SortOrder = PortalPublishRules.SortOrderAt(index);
            }

            return Task.FromResult<IEnumerable<PortalFile>>([]);
        }, cancellationToken);

    public Task<PortalContentResponse> SetHeroAsync(long projectId, PortalHeroRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, async (settings, items, _) =>
        {
            var item = items.SingleOrDefault(i => i.Id == request.ItemId && i.RemovedAt is null) ?? throw new NotFoundException(ItemNotFound);
            if (!PortalPublishRules.CanBeHero(item.File.ContentType))
            {
                throw new RequestValidationException(ErrorCodes.PortalHeroNotImage, "itemId", "Only a JPEG, PNG or WebP image can be the hero.");
            }

            var old = await HeroFilesAsync(settings.HeroFileId, cancellationToken);
            settings.HeroFileId = item.FileId;
            return old;
        }, cancellationToken);

    public Task<PortalContentResponse> ClearHeroAsync(long projectId, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, async (settings, _, _) =>
        {
            var old = await HeroFilesAsync(settings.HeroFileId, cancellationToken);
            settings.HeroFileId = null;
            return old;
        }, cancellationToken);

    public Task<PortalContentResponse> SetSummaryAsync(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, (settings, _, _) =>
        {
            settings.Summary = PortalPublishRules.CleanSummary(request.Summary);
            return Task.FromResult<IEnumerable<PortalFile>>([]);
        }, cancellationToken);

    public Task<PortalContentResponse> RestoreAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, (_, items, _) =>
        {
            var item = items.SingleOrDefault(i => i.Id == itemId) ?? throw new NotFoundException(ItemNotFound);
            item.RemovedAt = null;
            item.UpdatedAt = clock.UtcNow;
            item.UpdatedById = currentUser.Id;
            return Task.FromResult<IEnumerable<PortalFile>>([]);
        }, cancellationToken);

    public async Task<PortalContentResponse> PublishAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default)
    {
        var project = await EditableProjectAsync(projectId, cancellationToken);
        var now = clock.UtcNow;
        List<PortalUpload> cancelled = [];
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            RequireRevision(settings, request.ExpectedRevision);
            var items = (await content.ItemsToChangeAsync(projectId, cancellationToken)).ToList();
            var changes = PortalPublishRules.ChangesOf(
                items.Select(Copies).ToList(), settings.HeroFileId, settings.PublishedHeroFileId, settings.Summary, settings.PublishedSummary);
            if (!changes.Any)
            {
                // nothing would change for clients
                return await views.ContentAsync(project, cancellationToken);
            }

            var candidates = new List<PortalFile>();
            var oldPublished = await content.FilesAsync(items.Where(i => i.PublishedFileId is { } p && p != i.FileId).Select(i => i.PublishedFileId!.Value), cancellationToken);
            candidates.AddRange(oldPublished);

            foreach (var item in items.Where(i => i.RemovedAt is not null).ToList())
            {
                cancelled.AddRange(await StopReplacementsAsync(item.Id, now, cancellationToken));
                candidates.Add(item.File);
                content.Remove(item);
                items.Remove(item);
            }

            foreach (var item in items)
            {
                item.PublishedFileId = item.FileId;
                item.PublishedTitle = item.Title;
                item.PublishedSection = item.Section;
                item.PublishedSortOrder = item.SortOrder;
            }

            if (settings.PublishedHeroFileId != settings.HeroFileId)
            {
                candidates.AddRange(await HeroFilesAsync(settings.PublishedHeroFileId, cancellationToken));
                settings.PublishedHeroFileId = settings.HeroFileId;
            }

            settings.PublishedSummary = settings.Summary;
            settings.PublishedRevision = settings.Revision;
            settings.PublishedAt = now;
            settings.PublishedById = currentUser.Id;
            released = PortalReferences.Release(candidates, items, settings, now);

            // one event per publication: what later notifications to clients hook into
            activity.Add(new ActivityEvent
            {
                DomainCode = project.DomainCode,
                Kind = ActivityEventKinds.PortalPublished,
                OccurredAt = now,
                ActorId = currentUser.Id,
                ProjectId = project.Id,
                ClientId = project.ClientId,
                Payload = JsonSerializer.Serialize(new { added = changes.Added, updated = changes.Updated, removed = changes.Removed, revision = settings.Revision }),
            });

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await AfterAsync(cancelled, released, cancellationToken);
        return await views.ContentAsync(project, cancellationToken);
    }

    public async Task<PortalContentResponse> DiscardAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default)
    {
        var project = await EditableProjectAsync(projectId, cancellationToken);
        var now = clock.UtcNow;
        List<PortalUpload> cancelled = [];
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            RequireRevision(settings, request.ExpectedRevision);
            if (settings.PublishedAt is null)
            {
                throw new BusinessRuleException(
                    ErrorCodes.PortalNeverPublished, "Nothing has been published yet, so there's no version to go back to.");
            }

            var items = (await content.ItemsToChangeAsync(projectId, cancellationToken)).ToList();
            var published = (await content.FilesAsync(items.Where(i => i.PublishedFileId is not null).Select(i => i.PublishedFileId!.Value), cancellationToken))
                .ToDictionary(f => f.Id);
            var candidates = new List<PortalFile>();

            foreach (var item in items.ToList())
            {
                if (item.PublishedFileId is not { } publishedFileId)
                {
                    // never published: it goes
                    cancelled.AddRange(await StopReplacementsAsync(item.Id, now, cancellationToken));
                    candidates.Add(item.File);
                    content.Remove(item);
                    items.Remove(item);
                    continue;
                }

                if (item.FileId != publishedFileId)
                {
                    candidates.Add(item.File);
                    item.File = published[publishedFileId];
                }

                item.Title = item.PublishedTitle!;
                item.Section = item.PublishedSection!.Value;
                item.SortOrder = item.PublishedSortOrder!.Value;
                item.RemovedAt = null;
                item.UpdatedAt = now;
                item.UpdatedById = currentUser.Id;
            }

            if (settings.HeroFileId != settings.PublishedHeroFileId)
            {
                candidates.AddRange(await HeroFilesAsync(settings.HeroFileId, cancellationToken));
                settings.HeroFileId = settings.PublishedHeroFileId;
            }

            settings.Summary = settings.PublishedSummary;
            settings.Revision++;
            released = PortalReferences.Release(candidates, items, settings, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await AfterAsync(cancelled, released, cancellationToken);
        return await views.ContentAsync(project, cancellationToken);
    }

    public async Task<PortalPresentationResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default)
        => await views.PresentationAsync(await views.VisibleProjectAsync(projectId, cancellationToken), published: false, cancellationToken);

    /// <summary>
    /// One draft change under the project's content lock: <paramref name="change"/> edits the settings and items and
    /// returns the files it may have let go; the revision moves on, and files nothing points at any more are deleted.
    /// </summary>
    private async Task<PortalContentResponse> ChangeAsync(
        long projectId,
        Func<PortalSettings, IReadOnlyList<PortalItem>, DateTime, Task<IEnumerable<PortalFile>>> change,
        CancellationToken cancellationToken)
    {
        var project = await EditableProjectAsync(projectId, cancellationToken);
        var now = clock.UtcNow;
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            var items = await content.ItemsToChangeAsync(projectId, cancellationToken);
            var candidates = await change(settings, items, now);
            settings.Revision++;
            released = PortalReferences.Release(candidates, items, settings, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await maintenance.DeleteReleasedAsync(released, cancellationToken);
        return await views.ContentAsync(project, cancellationToken);
    }

    private async Task<PortalContentProject> EditableProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var project = await views.VisibleProjectAsync(projectId, cancellationToken);
        if (PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status) is { } denial)
        {
            throw denial.ToException();
        }

        return project;
    }

    private static void RequireRevision(PortalSettings settings, int expected)
    {
        if (settings.Revision != expected)
        {
            throw new ConflictException(
                ErrorCodes.PortalRevisionStale, "The drafts changed since you looked. Check what's new, then try again.");
        }
    }

    private async Task<IEnumerable<PortalFile>> HeroFilesAsync(long? fileId, CancellationToken cancellationToken)
        => fileId is { } id ? await content.FilesAsync([id], cancellationToken) : [];

    /// <summary>Uploads on their way to replace the item's file are cancelled, and none of them points at it any more.</summary>
    private async Task<List<PortalUpload>> StopReplacementsAsync(long itemId, DateTime now, CancellationToken cancellationToken)
    {
        var cancelled = new List<PortalUpload>();
        foreach (var upload in await content.UploadsReplacingAsync(itemId, cancellationToken))
        {
            if (upload.Status == PortalUploadStatus.Uploading)
            {
                upload.Status = PortalUploadStatus.Cancelled;
                upload.LastActivityAt = now;
                cancelled.Add(upload);
            }

            upload.ReplaceItemId = null;
        }

        return cancelled;
    }

    private async Task AfterAsync(List<PortalUpload> cancelled, List<PortalFile> released, CancellationToken cancellationToken)
    {
        foreach (var upload in cancelled)
        {
            await maintenance.RemoveUploadAsync(upload, cancellationToken);
        }

        await maintenance.DeleteReleasedAsync(released, cancellationToken);
    }

    private static PortalItemCopies Copies(PortalItem i)
        => new(i.Id, i.FileId, i.Title, i.Section, i.SortOrder, i.PublishedFileId, i.PublishedTitle, i.PublishedSection, i.PublishedSortOrder, i.RemovedAt is not null);
}
