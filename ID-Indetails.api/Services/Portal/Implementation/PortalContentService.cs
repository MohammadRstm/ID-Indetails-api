using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// Staff preparing a Design project's client portal content (client_portal:manage). The browser sends each file in
/// parts straight to the content store; the API only hands out short-lived links, then checks what arrived before
/// adding it. The upload row is written before anything is sent, so the store never holds an object no row knows
/// about; the cleanup removes what an abandoned upload left. Execution projects have no portal and read as not found.
/// </summary>
public sealed class PortalContentService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPortalContentRepository content,
    IContentStore store,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PortalContentOptions> options,
    ILogger<PortalContentService> logger) : IPortalContentService
{
    private const string ProjectNotFound = "Project not found.";
    private const string UploadNotFound = "Upload not found.";
    private const string ItemNotFound = "File not found.";

    public async Task<PortalContentResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        var items = await content.ItemsAsync(projectId, cancellationToken);
        var denial = PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status);
        var previewExpiresAt = clock.UtcNow.AddMinutes(options.Value.PreviewLinkMinutes);

        var described = new List<PortalContentItemResponse>(items.Count);
        foreach (var item in items)
        {
            described.Add(await DescribeAsync(item, previewExpiresAt, cancellationToken));
        }

        return new PortalContentResponse
        {
            ProjectId = projectId,
            StorageReady = store.IsAvailable,
            CanEdit = store.IsAvailable && denial is null,
            ReadOnlyReason = denial?.Message,
            Usage = Usage(await content.UsageAsync(projectId, cancellationToken)),
            AcceptedExtensions = PortalContentRules.AcceptedExtensions,
            Items = described,
        };
    }

    public async Task<PortalUploadStartedResponse> StartUploadAsync(
        long projectId, StartPortalUploadRequest request, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));
        RequireStore();

        var name = PortalContentRules.SafeName(request.FileName);
        var type = PortalContentRules.TypeOf(name)
            ?? throw new RequestValidationException(
                ErrorCodes.PortalFileTypeNotAllowed, "fileName",
                $"The client portal doesn't take this kind of file. It takes: {string.Join(", ", PortalContentRules.AcceptedExtensions)}.");
        var now = clock.UtcNow;

        // the same file started again (a retried request) carries on with its upload
        var existing = await content.FindUploadAsync(currentUser.Id, request.ClientToken, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProjectId != projectId || existing.Status is not (PortalUploadStatus.Uploading or PortalUploadStatus.Completed))
            {
                throw Ended();
            }

            existing.LastActivityAt = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Started(existing);
        }

        if (request.ReplaceItemId is { } replaceItemId && !await content.ItemExistsAsync(projectId, replaceItemId, cancellationToken))
        {
            throw new NotFoundException(ItemNotFound);
        }

        // written before anything reaches the store, so the cleanup can always find the key
        var upload = new PortalUpload
        {
            ProjectId = projectId,
            Section = request.Section!.Value,
            ReplaceItemId = request.ReplaceItemId,
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
        await unitOfWork.SaveChangesAsync(cancellationToken);

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
        await VisibleProjectAsync(projectId, cancellationToken);
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
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        RequireStore();
        var upload = await OwnUploadAsync(projectId, uploadId, cancellationToken);
        if (upload.Status == PortalUploadStatus.Completed)
        {
            return await CompletedAsync(projectId, upload.StorageKey, cancellationToken);
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

        var replaced = await AddToProjectAsync(projectId, upload.Id, type, storedSize, cancellationToken);
        if (replaced is not null)
        {
            await DeleteFileAsync(replaced, cancellationToken);
        }

        return await CompletedAsync(projectId, upload.StorageKey, cancellationToken);
    }

    public async Task CancelUploadAsync(long projectId, long uploadId, CancellationToken cancellationToken = default)
    {
        await VisibleProjectAsync(projectId, cancellationToken);
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

        await RemoveFromStoreAsync(upload, cancellationToken);
    }

    public async Task<PortalContentItemResponse> UpdateItemAsync(
        long projectId, long itemId, UpdatePortalItemRequest request, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var item = await content.GetItemForUpdateAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);
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
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var view = await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);
        return await DescribeAsync(view, clock.UtcNow.AddMinutes(options.Value.PreviewLinkMinutes), cancellationToken);
    }

    public async Task<PortalUsageChangedResponse> DeleteItemAsync(long projectId, long itemId, CancellationToken cancellationToken = default)
    {
        var project = await VisibleProjectAsync(projectId, cancellationToken);
        Throw(PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status));

        var now = clock.UtcNow;
        var cancelled = new List<PortalUpload>();
        PortalFile file;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var item = await content.GetItemForUpdateAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);

            // a replacement still on its way has nothing left to replace
            foreach (var upload in await content.UploadsReplacingAsync(item.Id, cancellationToken))
            {
                if (upload.Status == PortalUploadStatus.Uploading)
                {
                    upload.Status = PortalUploadStatus.Cancelled;
                    upload.LastActivityAt = now;
                    cancelled.Add(upload);
                }

                upload.ReplaceItemId = null;
            }

            file = item.File;
            file.DeletedAt = now;
            content.Remove(item);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var upload in cancelled)
        {
            await RemoveFromStoreAsync(upload, cancellationToken);
        }

        await DeleteFileAsync(file, cancellationToken);
        return new PortalUsageChangedResponse(Usage(await content.UsageAsync(projectId, cancellationToken)));
    }

    public async Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default)
    {
        await VisibleProjectAsync(projectId, cancellationToken);
        RequireStore();
        var item = await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);

        var showInline = inline && PortalContentRules.PreviewOf(item.File.ContentType) != PortalPreview.None;
        var expiresAt = clock.UtcNow.AddMinutes(options.Value.DownloadLinkMinutes);
        var url = await store.GetDownloadUrlAsync(
            item.File.StorageKey, item.File.OriginalName, item.File.ContentType, showInline, expiresAt, cancellationToken);
        return new PortalContentLinkResponse(url, expiresAt);
    }

    /// <summary>A Design project the caller manages client portal content on. Anything else reads as not found.</summary>
    private async Task<PortalContentProject> VisibleProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ClientPortalManage);
        var project = await content.GetProjectAsync(projectId, cancellationToken);
        return project is not null && project.DomainCode == DomainCodes.Design && actor.CanSee(project.DomainCode)
            ? project
            : throw new NotFoundException(ProjectNotFound);
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
    /// Adds the checked file to its section, or swaps it in for the replaced item's, in one transaction with the upload
    /// locked. Returns the replaced file, whose object is deleted once this is saved. If the upload was cancelled in the
    /// meantime, nothing is added and the object is removed.
    /// </summary>
    private async Task<PortalFile?> AddToProjectAsync(
        long projectId, long uploadId, PortalContentRules.FileType type, long storedSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        unitOfWork.DiscardChanges();
        PortalUpload upload;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            upload = await content.GetUploadForUpdateAsync(uploadId, cancellationToken) ?? throw new NotFoundException(UploadNotFound);
            if (upload.Status == PortalUploadStatus.Completed)
            {
                return null;
            }

            if (upload.Status == PortalUploadStatus.Uploading)
            {
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

                PortalFile? replaced = null;
                if (upload.ReplaceItemId is { } replaceItemId)
                {
                    var item = await content.GetItemForUpdateAsync(projectId, replaceItemId, cancellationToken)
                        ?? throw new NotFoundException(ItemNotFound);
                    replaced = item.File;
                    replaced.DeletedAt = now;
                    item.File = file;
                    item.UpdatedAt = now;
                    item.UpdatedById = currentUser.Id;
                }
                else
                {
                    content.Add(new PortalItem
                    {
                        ProjectId = projectId,
                        Section = upload.Section,
                        File = file,
                        Title = PortalContentRules.DefaultTitle(upload.FileName),
                        SortOrder = PortalContentRules.NextSortOrder(await content.LastSortOrderAsync(projectId, upload.Section, cancellationToken)),
                        CreatedAt = now,
                        UpdatedAt = now,
                        UpdatedById = currentUser.Id,
                    });
                }

                upload.Status = PortalUploadStatus.Completed;
                upload.LastActivityAt = now;
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return replaced;
            }
        }

        // cancelled (or its item deleted) while the parts were being joined
        await RemoveFromStoreAsync(upload, cancellationToken);
        throw Ended();
    }

    private async Task<PortalUploadCompletedResponse> CompletedAsync(long projectId, string storageKey, CancellationToken cancellationToken)
    {
        var itemId = await content.ItemIdForKeyAsync(storageKey, cancellationToken)
            ?? throw new NotFoundException(ItemNotFound); // completed, then replaced or deleted since
        var view = await content.ItemViewAsync(projectId, itemId, cancellationToken) ?? throw new NotFoundException(ItemNotFound);
        var item = await DescribeAsync(view, clock.UtcNow.AddMinutes(options.Value.PreviewLinkMinutes), cancellationToken);
        return new PortalUploadCompletedResponse(item, Usage(await content.UsageAsync(projectId, cancellationToken)));
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

    /// <summary>Best effort: drops an ended upload's parts and object. What fails here, the cleanup does later.</summary>
    private async Task RemoveFromStoreAsync(PortalUpload upload, CancellationToken cancellationToken)
    {
        try
        {
            await store.AbortUploadAsync(upload.StorageKey, upload.StoreUploadId, cancellationToken);
            await store.DeleteAsync(upload.StorageKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not remove ended upload {UploadId} from the content store; the cleanup will.", upload.Id);
        }
    }

    /// <summary>Best effort: deletes a file no longer used, object first. What fails here, the cleanup does later.</summary>
    private async Task DeleteFileAsync(PortalFile file, CancellationToken cancellationToken)
    {
        try
        {
            await store.DeleteAsync(file.StorageKey, cancellationToken);
            content.Remove(file);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not delete client portal file {FileId} yet; the cleanup will.", file.Id);
        }
    }

    private async Task<PortalContentItemResponse> DescribeAsync(PortalItemView item, DateTime previewExpiresAt, CancellationToken cancellationToken)
    {
        var preview = PortalContentRules.PreviewOf(item.File.ContentType);
        var previewUrl = store.IsAvailable && preview is PortalPreview.Image or PortalPreview.Video
            ? await store.GetDownloadUrlAsync(item.File.StorageKey, item.File.OriginalName, item.File.ContentType, true, previewExpiresAt, cancellationToken)
            : null;

        return new PortalContentItemResponse
        {
            Id = item.Id,
            Section = item.Section,
            Title = item.Title,
            SortOrder = item.SortOrder,
            IsDraft = true,
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
            PreviewUrlExpiresAt = previewUrl is null ? null : previewExpiresAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedByName is null ? null : new PersonRefResponse(item.UpdatedById, item.UpdatedByName),
        };
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
