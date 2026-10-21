using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Files;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Files.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// Photos are stored made small (<see cref="Pictures"/>): upright, at most <see cref="Pictures.MaxEdge"/> px on their
/// longest side, as WebP. Videos are stored as they come, written straight to disk as they arrive.
/// Who may download a file: whoever uploaded it; any signed-in person for a profile photo or the company logo; the
/// chat's people for a message attachment; whoever may read the work entry for its attachment. Anyone else gets
/// not found. Uploads attached to nothing within a day are deleted.
/// </summary>
public sealed class FileService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IFileRepository files,
    IFileStorage storage,
    IPictureSizes sizes,
    IWorkEntryRepository entries,
    IMessagingService messaging,
    IUnitOfWork unitOfWork,
    IClock clock) : IFileService
{
    public static readonly TimeSpan UnusedFor = TimeSpan.FromDays(1);
    private const int CleanupBatch = 200;
    private const string NotFound = "File not found.";

    public async Task<FileResponse> UploadAsync(Stream content, string? fileName, long uploadLength, CancellationToken cancellationToken = default)
    {
        var me = await ActiveAsync(cancellationToken);
        var header = new byte[FileRules.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var detected = FileRules.Detect(header.AsSpan(0, read))
            ?? throw new BusinessRuleException(
                ErrorCodes.FileTypeNotAllowed, "Only photos (JPEG, PNG, WebP, HEIC) and videos (MP4, MOV, WebM) can be uploaded.");
        // the upload's length (the file and its small envelope) is checked before anything is stored
        FileRules.CheckDailyQuota(await files.BytesUploadedSinceAsync(me.UserId, clock.UtcNow.AddDays(-1), cancellationToken), uploadLength).ThrowIfDenied();

        var whole = new PrefixedStream(header.AsMemory(0, read), content);
        var (key, stored) = detected.Kind == FileKind.Image
            ? await SavePhotoAsync(whole, detected, cancellationToken)
            : await SaveVideoAsync(whole, detected, cancellationToken);
        var file = new StoredFile
        {
            StorageKey = key,
            OriginalName = FileRules.SafeName(fileName, stored.Extension),
            MimeType = stored.MimeType,
            SizeBytes = storage.SizeOf(key),
            UploadedById = me.UserId,
            CreatedAt = clock.UtcNow,
        };
        files.Add(file);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            storage.Delete(key);
            throw;
        }

        return FileMapper.ToResponse(file);
    }

    /// <summary>A photo, read whole (it is at most 10 MB) and stored made small; as it came when it can't be read.</summary>
    private async Task<(string Key, FileRules.Detected Stored)> SavePhotoAsync(Stream photo, FileRules.Detected detected, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await photo.CopyToAsync(buffer, cancellationToken);
        FileRules.CheckSize(detected.Kind, buffer.Length).ThrowIfDenied();

        var original = buffer.ToArray();
        return Pictures.Shrink(original, Pictures.MaxEdge) is { } small
            ? (await storage.SaveAsync(new MemoryStream(small), Pictures.WebpExtension, cancellationToken),
                detected with { MimeType = Pictures.WebpMimeType, Extension = Pictures.WebpExtension })
            : (await storage.SaveAsync(new MemoryStream(original), detected.Extension, cancellationToken), detected);
    }

    /// <summary>A video, written to disk as it arrives; refused (and removed) when it turns out too big.</summary>
    private async Task<(string Key, FileRules.Detected Stored)> SaveVideoAsync(Stream video, FileRules.Detected detected, CancellationToken cancellationToken)
    {
        var key = await storage.SaveAsync(video, detected.Extension, cancellationToken);
        if (FileRules.CheckSize(detected.Kind, storage.SizeOf(key)) is { } tooBig)
        {
            storage.Delete(key);
            tooBig.ThrowIfDenied();
        }

        return (key, detected);
    }

    public async Task<FileContent> OpenAsync(long id, int? size = null, CancellationToken cancellationToken = default)
    {
        var me = await ActiveAsync(cancellationToken);
        var file = await files.GetAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound);
        if (file.UploadedById != me.UserId && !await CanReadAsync(me, await files.UsesAsync(id, cancellationToken), cancellationToken))
        {
            throw new NotFoundException(NotFound);
        }

        return await sizes.OpenAsync(file, size, cancellationToken);
    }

    public async Task<int> DeleteUnusedAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow - UnusedFor;
        var deleted = 0;
        while (await files.UnusedBeforeAsync(cutoff, CleanupBatch, cancellationToken) is { Count: > 0 } batch)
        {
            foreach (var file in batch)
            {
                files.Remove(file);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // The rows are gone first: a stray file on disk is harmless, a row without its file isn't.
            foreach (var file in batch)
            {
                storage.Delete(file.StorageKey);
            }

            deleted += batch.Count;
        }

        return deleted;
    }

    private async Task<bool> CanReadAsync(UserAccess me, FileUses uses, CancellationToken cancellationToken)
    {
        // the website's pictures are public anyway
        if (uses.IsPhoto || uses.IsLogo || uses.IsWebsite)
        {
            return true;
        }

        if (uses.EntryIds.Count > 0
            && (await entries.ViewsByIdAsync(uses.EntryIds, cancellationToken)).Any(e => WorkEntryQueryService.CanRead(me, e)))
        {
            return true;
        }

        foreach (var conversationId in uses.ConversationIds)
        {
            if (await messaging.CanReadConversationAsync(conversationId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The caller, who must still be active: an access token outlives a deactivation.</summary>
    private async Task<UserAccess> ActiveAsync(CancellationToken cancellationToken)
    {
        var me = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return me.Permissions.Count > 0 ? me : throw new ForbiddenException(ErrorCodes.Forbidden, "You don't have permission to do this.");
    }
}
