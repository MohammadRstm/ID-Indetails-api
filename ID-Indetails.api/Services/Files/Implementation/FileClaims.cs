using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Files;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Files.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>Only the uploader attaches a file, and only once: someone else's upload reads as not found.</summary>
public sealed class FileClaims(ICurrentUser currentUser, IFileRepository files, IFileStorage storage) : IFileClaims
{
    public async Task<StoredFile> ClaimWebsitePictureAsync(long id, CancellationToken cancellationToken = default)
    {
        var file = await ClaimAsync(id, FileKind.Image, cancellationToken);
        using var buffer = new MemoryStream();
        await using (var content = storage.OpenRead(file.StorageKey))
        {
            await content.CopyToAsync(buffer, cancellationToken);
        }

        return Pictures.CanCopy(buffer.ToArray())
            ? file
            : throw new BusinessRuleException(ErrorCodes.FileTypeNotAllowed,
                "The website can't show this picture (an iPhone HEIC photo, for one). Save it as JPEG or PNG and upload it again.");
    }

    public async Task<StoredFile> ClaimAsync(long id, FileKind? kind, CancellationToken cancellationToken = default)
    {
        var file = await files.GetForUpdateAsync(id, cancellationToken);
        if (file is null || file.UploadedById != currentUser.Id)
        {
            throw new NotFoundException("File not found.");
        }

        if ((await files.UsesAsync(id, cancellationToken)).IsUsed)
        {
            throw new BusinessRuleException(ErrorCodes.FileInUse, "This file is already attached to something. Upload it again.");
        }

        return kind is null || FileRules.KindOf(file.MimeType) == kind
            ? file
            : throw new BusinessRuleException(ErrorCodes.FileTypeNotAllowed, kind == FileKind.Image ? "This needs a photo." : "This needs a video.");
    }
}
