using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;

namespace ID_Indetails.api.Repositories.Activity.Implementation;

public sealed class ActivityEventRepository(IdIndetailsDbContext db) : IActivityEventRepository
{
    public void Add(ActivityEvent activityEvent) => db.ActivityEvents.Add(activityEvent);
}
