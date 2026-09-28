using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Notifications.Implementation;

public sealed class NotificationRepository(IdIndetailsDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public Task<Notification?> GetDigestForUpdateAsync(long recipientId, string digestKey, CancellationToken cancellationToken = default)
        => db.Notifications.SingleOrDefaultAsync(n => n.RecipientId == recipientId && n.DigestKey == digestKey, cancellationToken);
}
