namespace ID_Indetails.api.DTO.Request.Files;

/// <summary>A photo the caller uploaded (<c>POST /api/files</c>) and hasn't attached elsewhere.</summary>
public sealed record AttachFileRequest
{
    public long FileId { get; init; }
}
