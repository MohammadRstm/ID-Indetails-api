using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Files;

/// <summary>An uploaded photo or video. Its content is at <c>GET /api/files/{id}</c>.</summary>
public sealed record FileResponse(long Id, string Name, string MimeType, FileKind Kind, long SizeBytes, DateTime CreatedAt);
