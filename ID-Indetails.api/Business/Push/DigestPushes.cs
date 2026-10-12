namespace ID_Indetails.api.Business.Push;

/// <summary>
/// Rolling digests (work logged today, requirements added today) change all day. A person gets at most one full push of
/// each an hour; the updates in between only refresh the app icon's badge.
/// </summary>
public static class DigestPushes
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    public static bool PushesInFull(DateTime? lastFullPush, DateTime now) => lastFullPush is not { } last || now - last >= Every;
}
