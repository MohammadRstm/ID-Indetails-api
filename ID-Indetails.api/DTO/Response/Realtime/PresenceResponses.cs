namespace ID_Indetails.api.DTO.Response.Realtime;

/// <param name="Online">The ids of the people the caller can see who have the live connection open.</param>
public sealed record PresenceResponse(IReadOnlyList<long> Online);
