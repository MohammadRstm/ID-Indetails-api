using ID_Indetails.api.Business.Files;
using ID_Indetails.api.DTO.Response.Files;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Files.Abstraction;

namespace ID_Indetails.api.Services.Files.Implementation;

public static class FileMapper
{
    public static FileResponse ToResponse(FileView f) => new(f.Id, f.Name, f.MimeType, FileRules.KindOf(f.MimeType), f.SizeBytes, f.CreatedAt);

    public static FileResponse ToResponse(StoredFile f) => new(f.Id, f.OriginalName, f.MimeType, FileRules.KindOf(f.MimeType), f.SizeBytes, f.CreatedAt);
}
