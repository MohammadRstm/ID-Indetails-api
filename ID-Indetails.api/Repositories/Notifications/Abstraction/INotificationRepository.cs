using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Notifications.Abstraction;

public interface INotificationRepository
{
    /// <summary>Stages a new notification; saved by the unit of work.</summary>
    void Add(Notification notification);
}
