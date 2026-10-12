using ID_Indetails.api.Business.Push;

namespace ID_Indetails.api.Services.Push.Abstraction;

/// <summary>One push to one device, already decided and written.</summary>
/// <param name="Title">Null with <paramref name="Body"/> for a push that only updates the app icon's badge.</param>
/// <param name="Sound">Whether the phone plays its sound.</param>
/// <param name="ChannelId">The Android channel.</param>
/// <param name="Quiet">Delivered silently: low importance on Android, passive on iOS.</param>
public sealed record PushMessage(
    string To,
    string? Title,
    string? Body,
    PushData Data,
    bool Sound,
    int Badge,
    string ChannelId,
    bool Quiet);

/// <summary>What the push service said about one message: an id to check its receipt later, or an error.</summary>
public sealed record PushTicket(string? Id, string? Error);

/// <summary>
/// The only part that knows who delivers pushes (<c>ExpoPushProvider</c>), so FCM and APNs could replace it without
/// touching the rest.
/// </summary>
public interface IPushProvider
{
    /// <summary>The device's push token isn't valid any more: the app was removed, or the token replaced.</summary>
    public const string DeviceNotRegistered = "DeviceNotRegistered";

    /// <summary>One ticket per message, in the same order.</summary>
    Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>The error of each ticket whose receipt reports one; tickets without a receipt yet are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> ReceiptErrorsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken = default);
}
