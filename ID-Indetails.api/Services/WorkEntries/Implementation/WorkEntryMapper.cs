using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

internal static class WorkEntryMapper
{
    public static WorkEntryResponse ToResponse(WorkEntryView e, long viewerId, DomainSettings settings, DateTime now) => new(
        e.Id,
        new WorkEntryPersonResponse(e.UserId, e.UserName),
        new WorkEntryProjectResponse(e.ProjectId, e.ProjectTitle),
        e.Description,
        e.StartedAt,
        e.EndedAt,
        e.Minutes,
        e.CreatedAt,
        e.UpdatedAt,
        e.UserId == viewerId
            && WorkEntryRules.IsWithinEditWindow(e.CreatedAt, now, settings.EditWindow)
            && WorkEntryRules.AcceptsWork(e.ProjectStatus, settings.HoldLogging),
        e.CommentCount);
}
