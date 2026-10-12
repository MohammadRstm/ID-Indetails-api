namespace ID_Indetails.api.Business.Push;

/// <summary>
/// A push's data payload, which Apple, Google and Expo can see too: ids and kinds only, never text or amounts.
/// <paramref name="ForUserId"/> lets the app ignore a push meant for someone else signed in on a shared phone.
/// </summary>
/// <param name="Kind">A notification kind, or one of <c>PushKinds</c>.</param>
/// <param name="NotificationId">Set for pushes of a notification.</param>
public sealed record PushData(long ForUserId, string Kind, long? NotificationId, PushTarget Target);
