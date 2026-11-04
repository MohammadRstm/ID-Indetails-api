namespace ID_Indetails.api.Enums;

/// <summary>Who delivers a device's pushes. FCM and APNs directly could follow without a new table.</summary>
public enum PushProvider
{
    /// <summary>The phone app, through Expo's push service.</summary>
    Expo,

    /// <summary>The website in a browser or installed on a phone, through the browser's push service (Web Push).</summary>
    WebPush,
}
