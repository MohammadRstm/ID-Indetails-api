using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Notifications.Abstraction;

namespace ID_Indetails.api.Repositories.Notifications.Implementation;

public sealed class NotificationRepository(IdIndetailsDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);
}
