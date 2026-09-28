using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>Tells the domain's admins about newly logged work, following the domain's <c>notify_entry</c> setting.</summary>
public interface IWorkLogNotifier
{
    /// <summary>Stages the notifications; saved with the caller's unit of work. Call inside its transaction.</summary>
    Task NotifyAsync(WorkEntry entry, string projectTitle, string authorName, EntryNotificationMode mode, CancellationToken cancellationToken = default);
}
