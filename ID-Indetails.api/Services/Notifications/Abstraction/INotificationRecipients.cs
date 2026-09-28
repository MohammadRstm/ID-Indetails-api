namespace ID_Indetails.api.Services.Notifications.Abstraction;

/// <summary>Works out who should hear about something.</summary>
public interface INotificationRecipients
{
    /// <summary>Active people of <paramref name="domain"/> who hold <paramref name="permission"/>, except <paramref name="exceptUserId"/>.</summary>
    Task<IReadOnlyList<long>> HoldersAsync(string domain, string permission, long exceptUserId, CancellationToken cancellationToken = default);
}
