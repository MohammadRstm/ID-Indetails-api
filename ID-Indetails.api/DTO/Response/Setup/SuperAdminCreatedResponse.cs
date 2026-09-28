namespace ID_Indetails.api.DTO.Response.Setup;

/// <summary>The generated password is only ever sent by email, never returned here.</summary>
public sealed record SuperAdminCreatedResponse(long UserId, string Email);
