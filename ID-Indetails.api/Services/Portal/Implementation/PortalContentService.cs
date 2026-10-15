using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// Staff preparing a Design project's client portal content (client_portal:manage). The browser sends each file in
/// parts straight to the content store; the API only hands out short-lived links, then checks what arrived before
/// adding it. The upload row is written before anything is sent, so the store never holds an object no row knows
/// about; the cleanup removes what an abandoned upload left. Every change here is to the draft: clients see the
/// published copy until someone publishes (<see cref="PortalPublishingService"/>). Execution projects have no portal
/// and read as not found.
/// </summary>
public sealed class PortalContentService(
    ICurrentUser currentUser,
    IPortalContentViews views,
    IPortalContentRepository content,
    IPortalContentMaintenance maintenance,
    IContentStore store,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PortalContentOptions> options) : IPortalContentService
{
    private const string UploadNotFound = "Upload not found.";
    private const string ItemNotFound = "File not found.";

    public async Task<PortalContentResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
        => await views.ContentAsync(await views.VisibleProjectAsync(projectId, cancellationToken), cancellationToken);

    public async Task<PortalUploadStartedResponse> StartUploadAsync(
        long projectId, StartPortalUploadRequest request, CancellationToken cancellationToken = default)
    {
        var project = await views.VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));
        RequireStore();

        var name = PortalContentRules.SafeName(request.FileName);
        var type = PortalContentRules.TypeOf(name)
            ?? throw new RequestValidationException(
                ErrorCodes.PortalFileTypeNotAllowed, "fileName",
                $"The client portal doesn't take this kind of file. It takes: {string.Join(", ", PortalContentRules.AcceptedExtensions)}.");
        if (request.Hero && !PortalPublishRules.CanBeHero(type.ContentType))
        {
            throw new RequestValidationException(ErrorCodes.PortalHeroNotImage, "fileName", "The hero image must be a JPEG, PNG or WebP image.");
        }

        var now = clock.UtcNow;
        var existing = await content.FindUploadAsync(currentUser.Id, request.ClientToken, cancellationToken);
        if (existing is not null)
        {
            return await StartedAgainAsync(projectId, existing, now, cancellationToken);
        }

        if (!request.Hero && request.ReplaceItemId is { } replaceItemId)
        {
            // gone, or removed in the draft: there is nothing to replace
            var target = await content.ItemViewAsync(projectId, replaceItemId, cancellationToken);
            if (target is null || target.RemovedAt is not null)
            {
                throw new NotFoundException(ItemNotFound);
            }
        }

        // written before anything reaches the store, so the cleanup can always find the key
        var upload = new PortalUpload
        {
            ProjectId = projectId,
            Section = request.Hero ? null : request.Section!.Value,
            IsHero = request.Hero,
            ReplaceItemId = request.Hero ? null : request.ReplaceItemId,
            ClientToken = request.ClientToken,
            StorageKey = PortalContentRules.NewStorageKey(projectId),
            FileName = name,
            SizeBytes = request.Size,
            Status = PortalUploadStatus.Uploading,
            CreatedById = currentUser.Id,
            CreatedAt = now,
            LastActivityAt = now,
        };
        content.Add(upload);
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            // the same file started by another request at the same moment
            var other = await content.FindUploadAsync(currentUser.Id, request.ClientToken, cancellationToken) ?? throw Ended();
            return await StartedAgainAsync(projectId, other, now, cancellationToken);
        }

        try
        {
            upload.StoreUploadId = await store.StartUploadAsync(upload.StorageKey, type.ContentType, cancellationToken);
        }
        catch
        {
            // nothing reached the store: the row has nothing to clean up
            content.Remove(upload);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Started(upload);
    }

    public async Task<PortalPartLinksResponse> PartLinksAsync(
        long projectId, long uploadId, PortalPartLinksRequest request, CancellationToken cancellationToken = default)
    {
        await views.VisibleProjectAsync(projectId, cancellationToken);
        RequireStore();
        var upload = await OwnUploadAsync(projectId, uploadId, cancellationToken);
        if (upload.Status != PortalUploadStatus.Uploading)
        {
            throw Ended();
        }

        var partSize = PortalContentRules.PartSizeFor(upload.SizeBytes);
        var count = PortalContentRules.PartCountFor(upload.SizeBytes, partSize);
        if (request.PartNumbers.Any(n => n > count))
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "partNumbers", $"This upload has {count} parts.");
        }

        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(options.Value.PartLinkMinutes);
        var links = new List<PortalPartLinkResponse>();
        foreach (var number in request.PartNumbers.Distinct().Order())
        {
            links.Add(new(number, await store.GetPartUploadUrlAsync(upload.StorageKey, upload.StoreUploadId, number, expiresAt, cancellationToken)));
        }

        upload.LastActivityAt = now;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PortalPartLinksResponse(links, expiresAt);
    }

    public async Task<PortalUploadCompletedResponse> CompleteUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default)
    {
        var project = await views.VisibleProjectAsync(projectId, cancellationToken);
        RequireStore();
        var upload = await OwnUploadAsync(projectId, uploadId, cancellationToken);
        if (upload.Status == PortalUploadStatus.Completed)
        {
            return await CompletedAsync(projectId, upload, cancellationToken);
        }

        if (upload.Status != PortalUploadStatus.Uploading)
        {
            throw Ended();
        }

        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));
        upload.LastActivityAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var type = PortalContentRules.TypeOf(upload.FileName)!;
        var storedSize = await JoinPartsAsync(upload, cancellationToken);
        if (storedSize != upload.SizeBytes)
        {
            await RejectAsync(upload, "The file that arrived isn't the size that was announced. Upload it again.", cancellationToken);
        }

        var header = await store.ReadStartAsync(upload.StorageKey, PortalContentRules.HeaderLength, cancellationToken);
        if (!PortalContentRules.MatchesContent(type, header))
        {
            await RejectAsync(upload, $"This isn't a real .{type.Extension} file, so it was refused.", cancellationToken);
        }

        var released = await AddToProjectAsync(projectId, upload.Id, type, storedSize, cancellationToken);
        await maintenance.DeleteReleasedAsync(released, cancellationToken);
        return await CompletedAsync(projectId, upload, cancellationToken);
    }

    public async Task CancelUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default)
    {
        await views.VisibleProjectAsync(projectId, cancellationToken);
        await OwnUploadAsync(projectId, uploadId, cancellationToken);

        // locked, so it can't cross a completion that is adding the file right now
        unitOfWork.DiscardChanges();
        PortalUpload upload;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            upload = await content.GetUploadForUpdateAsync(uploadId, cancellationToken) ?? throw new NotFoundException(UploadNotFound);
            if (upload.Status != PortalUploadStatus.Uploading)
            {
                return;
            }

            upload.Status = PortalUploadStatus.Cancelled;
            upload.LastActivityAt = clock.UtcNow;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await maintenance.RemoveUploadAsync(upload, cancellationToken);
    }

    public async Task<PortalContentItemResponse> UpdateItemAsync(
        long projectId, long itemId, UpdatePortalItemRequest request, CancellationToken cancellationToken = default)
    {
        var project = await views.VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            var item = await content.GetItemForUpdateAsync(projectId, itemId, cancellationToken);
            if (item is null || item.RemovedAt is not null)
            {
                throw new NotFoundException(ItemNotFound);
            }

            if (request.Title is not null)
            {
                item.Title = request.Title.Trim();
            }

            if (request.Section is { } section && section != item.Section)
            {
                item.Section = section;
                item.SortOrder = PortalContentRules.NextSortOrder(await content.LastSortOrderAsync(projectId, section, cancellationToken));
            }

            item.UpdatedAt = clock.UtcNow;
            item.UpdatedById = currentUser.Id;
            settings.Revision++;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var view = await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);
        return await views.ItemAsync(view, (await content.SettingsAsync(projectId, cancellationToken)).HeroFileId, cancellationToken);
    }

    /// <summary>
    /// A file clients have never seen goes at once. A published one is only marked removed: clients keep it until the
    /// next publish, and the person can undo it until then.
    /// </summary>
    public async Task<PortalUsageChangedResponse> DeleteItemAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
    {
        var project = await views.VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));

        var now = clock.UtcNow;
        List<PortalUpload> cancelled;
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            var items = (await content.ItemsToChangeAsync(projectId, cancellationToken)).ToList();
            var item = items.SingleOrDefault(i => i.Id == itemId) ?? throw new NotFoundException(ItemNotFound);
            if (item.RemovedAt is not null)
            {
                return new PortalUsageChangedResponse(Usage(await content.UsageAsync(projectId, cancellationToken)));
            }

            // a replacement still on its way has nothing left to replace
            cancelled = await StopReplacementsAsync(item.Id, now, cancellationToken);

            var candidates = new List<PortalFile> { item.File };
            if (item.PublishedFileId is { } publishedFileId)
            {
                item.RemovedAt = now;
                if (item.FileId != publishedFileId)
                {
                    item.File = (await content.FilesAsync([publishedFileId], cancellationToken)).Single();
                }
            }
            else
            {
                content.Remove(item);
                items.Remove(item);
            }

            settings.Revision++;
            released = PortalReferences.Release(candidates, items, settings, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var upload in cancelled)
        {
            await maintenance.RemoveUploadAsync(upload, cancellationToken);
        }

        await maintenance.DeleteReleasedAsync(released, cancellationToken);
        return new PortalUsageChangedResponse(Usage(await content.UsageAsync(projectId, cancellationToken)));
    }

    public async Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default)
    {
        await views.VisibleProjectAsync(projectId, cancellationToken);
        RequireStore();
        var item = await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);

        var showInline = inline && PortalContentRules.PreviewOf(item.File.ContentType) != PortalPreview.None;
        var expiresAt = clock.UtcNow.AddMinutes(options.Value.DownloadLinkMinutes);
        var url = await store.GetDownloadUrlAsync(
            item.File.StorageKey, item.File.OriginalName, item.File.ContentType, showInline, expiresAt, cancellationToken);
        return new PortalContentLinkResponse(url, expiresAt);
    }

    /// <summary>
    /// The same file started again (a retried request) carries on with its upload, once the store has started it. One
    /// the store hasn't started (still starting, or its start failed) is ended: the browser starts again with a new token.
    /// </summary>
    private async Task<PortalUploadStartedResponse> StartedAgainAsync(
        long projectId, PortalUpload upload, DateTime now, CancellationToken cancellationToken)
    {
        if (upload.ProjectId != projectId
            || upload.Status is not (PortalUploadStatus.Uploading or PortalUploadStatus.Completed)
            || upload.StoreUploadId.Length == 0)
        {
            throw Ended();
        }

        upload.LastActivityAt = now;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Started(upload);
    }

    /// <summary>Only the person who started an upload can carry it on, complete or cancel it.</summary>
    private async Task<PortalUpload> OwnUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken)
    {
        var upload = await content.GetUploadAsync(projectId, uploadId, cancellationToken);
        return upload is not null && upload.CreatedById == currentUser.Id ? upload : throw new NotFoundException(UploadNotFound);
    }

    /// <summary>
    /// Joins the parts into the object, unless an earlier attempt already did (the store then has no such upload but
    /// has the object), and returns the object's size as the store reports it.
    /// </summary>
    private async Task<long> JoinPartsAsync(PortalUpload upload, CancellationToken cancellationToken)
    {
        var parts = await store.ListPartsAsync(upload.StorageKey, upload.StoreUploadId, cancellationToken);
        if (parts is not null)
        {
            var partSize = PortalContentRules.PartSizeFor(upload.SizeBytes);
            var count = PortalContentRules.PartCountFor(upload.SizeBytes, partSize);
            var missing = PortalContentRules.PartsNotReceived(parts.Select(p => (p.PartNumber, p.Size)), upload.SizeBytes, partSize);
            if (missing.Count > 0)
            {
                throw new ConflictException(
                    ErrorCodes.PortalUploadIncomplete,
                    $"{missing.Count} of {count} parts haven't fully arrived. Send them again, then complete the upload.");
            }

            await store.CompleteUploadAsync(
                upload.StorageKey, upload.StoreUploadId, parts.Where(p => p.PartNumber <= count).OrderBy(p => p.PartNumber).ToList(), cancellationToken);
        }

        var size = await store.GetSizeAsync(upload.StorageKey, cancellationToken);
        if (size is null)
        {
            // the store no longer has the upload (it gives up on unfinished ones after a while)
            upload.Status = PortalUploadStatus.Expired;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw Ended();
        }

        return size.Value;
    }

    /// <summary>
    /// Adds the checked file to its section, swaps it in for the replaced item's, or makes it the draft hero, in one
    /// transaction with the project's content locked. Returns the files nothing points at any more, whose objects are
    /// deleted once this is saved. If the upload was cancelled in the meantime, nothing is added and the object is removed.
    /// </summary>
    private async Task<IReadOnlyList<PortalFile>> AddToProjectAsync(
        long projectId, long uploadId, PortalContentRules.FileType type, long storedSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        unitOfWork.DiscardChanges();
        PortalUpload upload;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            upload = await content.GetUploadForUpdateAsync(uploadId, cancellationToken) ?? throw new NotFoundException(UploadNotFound);
            if (upload.Status == PortalUploadStatus.Completed)
            {
                return [];
            }

            if (upload.Status == PortalUploadStatus.Uploading)
            {
                var items = await content.ItemsToChangeAsync(projectId, cancellationToken);
                var file = new PortalFile
                {
                    ProjectId = projectId,
                    StorageKey = upload.StorageKey,
                    OriginalName = upload.FileName,
                    ContentType = type.ContentType,
                    OriginalSizeBytes = upload.SizeBytes,
                    StoredSizeBytes = storedSize,
                    UploadedById = upload.CreatedById,
                    CreatedAt = now,
                };
                content.Add(file);
                var candidates = new List<PortalFile>();

                if (upload.IsHero)
                {
                    // the new file needs its id before the settings can point at it
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    if (settings.HeroFileId is { } oldHero)
                    {
                        candidates.AddRange(await content.FilesAsync([oldHero], cancellationToken));
                    }

                    settings.HeroFileId = file.Id;
                }
                else if (upload.ReplaceItemId is { } replaceItemId)
                {
                    var item = items.SingleOrDefault(i => i.Id == replaceItemId && i.RemovedAt is null) ?? throw new NotFoundException(ItemNotFound);
                    candidates.Add(item.File);
                    item.File = file;
                    item.UpdatedAt = now;
                    item.UpdatedById = currentUser.Id;
                }
                else
                {
                    content.Add(new PortalItem
                    {
                        ProjectId = projectId,
                        Section = upload.Section!.Value,
                        File = file,
                        Title = PortalContentRules.DefaultTitle(upload.FileName),
                        SortOrder = PortalContentRules.NextSortOrder(await content.LastSortOrderAsync(projectId, upload.Section!.Value, cancellationToken)),
                        CreatedAt = now,
                        UpdatedAt = now,
                        UpdatedById = currentUser.Id,
                    });
                }

                upload.Status = PortalUploadStatus.Completed;
                upload.LastActivityAt = now;
                settings.Revision++;
                var released = PortalReferences.Release(candidates, items, settings, now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return released;
            }
        }

        // cancelled (or its item deleted) while the parts were being joined
        await maintenance.RemoveUploadAsync(upload, cancellationToken);
        throw Ended();
    }

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

    private async Task<PortalUploadCompletedResponse> CompletedAsync(long projectId, PortalUpload upload, CancellationToken cancellationToken)
    {
        var usage = Usage(await content.UsageAsync(projectId, cancellationToken));
        if (upload.IsHero)
        {
            return new PortalUploadCompletedResponse(null, usage);
        }

        var itemId = await content.ItemIdForKeyAsync(upload.StorageKey, cancellationToken)
            ?? throw new NotFoundException(ItemNotFound); // completed, then replaced or deleted since
        var view = await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);
        var hero = (await content.SettingsAsync(projectId, cancellationToken)).HeroFileId;
        return new PortalUploadCompletedResponse(await views.ItemAsync(view, hero, cancellationToken), usage);
    }

    /// <summary>Refuses what arrived: the object is deleted, the upload marked refused, and the uploader told why.</summary>
    private async Task RejectAsync(PortalUpload upload, string message, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(upload.StorageKey, cancellationToken);
        upload.Status = PortalUploadStatus.Rejected;
        upload.LastActivityAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        throw new RequestValidationException(ErrorCodes.PortalFileContentMismatch, "file", message);
    }

    private static PortalUploadStartedResponse Started(PortalUpload upload)
    {
        var partSize = PortalContentRules.PartSizeFor(upload.SizeBytes);
        return new PortalUploadStartedResponse(upload.Id, partSize, PortalContentRules.PartCountFor(upload.SizeBytes, partSize));
    }

    private static PortalUsageResponse Usage(PortalUsage usage) => new(usage.Files, usage.OriginalBytes, usage.StoredBytes);

    private static ConflictException Ended()
        => new(ErrorCodes.PortalUploadEnded, "This upload was cancelled, refused or has expired. Upload the file again.");

    private void RequireStore()
    {
        if (!store.IsAvailable)
        {
            throw new ServiceUnavailableException(
                ErrorCodes.ContentStorageUnavailable, "Client portal file storage isn't set up yet. Ask your administrator to configure it.");
        }
    }

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
