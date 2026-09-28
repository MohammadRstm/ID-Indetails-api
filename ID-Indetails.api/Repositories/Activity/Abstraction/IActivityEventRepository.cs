using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Activity.Abstraction;

/// <summary>Activity events are append-only: they are added, never updated or deleted.</summary>
public interface IActivityEventRepository
{
    /// <summary>Stages a new event; saved by the unit of work.</summary>
    void Add(ActivityEvent activityEvent);
}
