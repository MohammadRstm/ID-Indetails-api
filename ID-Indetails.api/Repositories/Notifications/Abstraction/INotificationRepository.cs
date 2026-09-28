using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Notifications.Abstraction;

public interface INotificationRepository
{
    /// <summary>Stages a new notification; saved by the unit of work.</summary>
    void Add(Notification notification);

    /// <summary>A recipient's rolling digest notification. Tracked.</summary>
    Task<Notification?> GetDigestForUpdateAsync(long recipientId, string digestKey, CancellationToken cancellationToken = default);
}
