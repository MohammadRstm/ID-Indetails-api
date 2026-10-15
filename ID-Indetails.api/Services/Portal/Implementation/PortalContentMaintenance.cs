using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// Keeps the content store and the rows in step. The rows lead: every object's key is written before the object
/// exists, so whatever an abandoned or half-finished upload left can be found and removed, and a file's object is
/// deleted before its row. The weekly check only reports unknown objects unless deleting them is switched on.
/// </summary>
public sealed class PortalContentMaintenance(
    IPortalContentRepository content,
    IContentStore store,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PortalContentOptions> options,
    ILogger<PortalContentMaintenance> logger) : IPortalContentMaintenance
{
    private const int Batch = 100;

    /// <summary>How long an ended upload's row is kept, so a repeated request still gets a sensible answer.</summary>
    private static readonly TimeSpan EndedKeptFor = TimeSpan.FromDays(7);

    public async Task<int> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        if (!store.IsAvailable)
        {
            return 0;
        }

        var handled = await ExpireAbandonedUploadsAsync(cancellationToken);
        handled += await DeleteUnusedFilesAsync(cancellationToken);
        handled += await ForgetEndedUploadsAsync(cancellationToken);
        return handled;
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (!store.IsAvailable)
        {
            return;
        }

        var known = await content.KnownKeysAsync(cancellationToken);
        var live = await content.LiveFileSizesAsync(cancellationToken);
        var graceCutoff = clock.UtcNow.AddDays(-options.Value.OrphanGraceDays);
        var seen = new HashSet<string>();
        int objects = 0, unknown = 0, deleted = 0, sizeDifferences = 0;
        long unknownBytes = 0;

        await foreach (var stored in store.ListAsync(PortalContentRules.KeyPrefix, cancellationToken))
        {
            objects++;
            seen.Add(stored.Key);
            if (!known.Contains(stored.Key))
            {
                // an upload started a moment ago may not be in the rows read above yet
                if (stored.LastModifiedUtc >= graceCutoff)
                {
                    continue;
                }

                unknown++;
                unknownBytes += stored.Size;
                if (options.Value.DeleteOrphans)
                {
                    await store.DeleteAsync(stored.Key, cancellationToken);
                    deleted++;
                }
                else
                {
                    logger.LogWarning("The content store holds {Key} ({Size} bytes), which no client portal row knows about.", stored.Key, stored.Size);
                }
            }
            else if (live.TryGetValue(stored.Key, out var size) && size != stored.Size)
            {
                sizeDifferences++;
                logger.LogWarning("Client portal file {Key} is {StoredSize} bytes in the store but {RecordedSize} in its row.", stored.Key, stored.Size, size);
            }
        }

        var missing = live.Keys.Where(key => !seen.Contains(key)).ToList();
        foreach (var key in missing)
        {
            logger.LogError("Client portal file {Key} is missing from the content store.", key);
        }

        logger.LogInformation(
            "Client portal storage check: {Objects} objects, {Unknown} unknown ({UnknownBytes} bytes, {Deleted} deleted), {Missing} missing, {SizeDifferences} size differences.",
            objects, unknown, unknownBytes, deleted, missing.Count, sizeDifferences);
    }

    /// <summary>Uploads with no request for a day: their parts and any joined object are removed, and they're marked expired.</summary>
    private async Task<int> ExpireAbandonedUploadsAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-options.Value.UploadInactivityHours);
        var handled = 0;
        foreach (var candidate in await content.AbandonedUploadsAsync(cutoff, Batch, cancellationToken))
        {
            await TryAsync(candidate.Id, async () =>
            {
                unitOfWork.DiscardChanges();
                await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

                // locked and checked again: a completion may have just picked it up
                var upload = await content.GetUploadForUpdateAsync(candidate.Id, cancellationToken);
                if (upload is null || upload.Status != PortalUploadStatus.Uploading || upload.LastActivityAt >= cutoff)
                {
                    return;
                }

                await RemoveFromStoreAsync(upload, cancellationToken);
                upload.Status = PortalUploadStatus.Expired;
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                handled++;
            });
        }

        return handled;
    }

    /// <summary>Files no longer used: the object goes first, then the row.</summary>
    private async Task<int> DeleteUnusedFilesAsync(CancellationToken cancellationToken)
    {
        unitOfWork.DiscardChanges();
        var handled = 0;
        foreach (var file in await content.FilesToDeleteAsync(Batch, cancellationToken))
        {
            await TryAsync(file.Id, async () =>
            {
                await store.DeleteAsync(file.StorageKey, cancellationToken);
                content.Remove(file);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                handled++;
            });
        }

        return handled;
    }

    /// <summary>Uploads that ended a week ago are forgotten; one that didn't complete has its leftovers removed first.</summary>
    private async Task<int> ForgetEndedUploadsAsync(CancellationToken cancellationToken)
    {
        unitOfWork.DiscardChanges();
        var handled = 0;
        foreach (var upload in await content.EndedUploadsAsync(clock.UtcNow - EndedKeptFor, Batch, cancellationToken))
        {
            await TryAsync(upload.Id, async () =>
            {
                // a completed upload's key belongs to its file now
                if (upload.Status != PortalUploadStatus.Completed && !await content.IsLiveFileKeyAsync(upload.StorageKey, cancellationToken))
                {
                    await RemoveFromStoreAsync(upload, cancellationToken);
                }

                content.Remove(upload);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                handled++;
            });
        }

        return handled;
    }

    private async Task RemoveFromStoreAsync(PortalUpload upload, CancellationToken cancellationToken)
    {
        if (upload.StoreUploadId.Length > 0)
        {
            await store.AbortUploadAsync(upload.StorageKey, upload.StoreUploadId, cancellationToken);
        }

        await store.DeleteAsync(upload.StorageKey, cancellationToken);
    }

    /// <summary>One row's failure (the store unreachable, say) is logged and retried on the next run; the others carry on.</summary>
    private async Task TryAsync(long id, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unitOfWork.DiscardChanges();
            logger.LogWarning(ex, "Client portal cleanup of row {Id} failed; it will be retried.", id);
        }
    }
}
