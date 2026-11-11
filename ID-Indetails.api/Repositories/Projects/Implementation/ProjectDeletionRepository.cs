using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Projects.Implementation;

public sealed class ProjectDeletionRepository(IdIndetailsDbContext db) : IProjectDeletionRepository
{
    public async Task<ProjectDeletionRules.Blocking> BlockingAsync(long projectId, DateTime openFromUtc, CancellationToken cancellationToken = default)
    {
        var hours = db.WorkEntries.Where(e => e.ProjectId == projectId && e.DeletedAt == null);
        return new ProjectDeletionRules.Blocking(
            IssuedInvoices: await db.Invoices.CountAsync(i => i.ProjectId == projectId && i.IssuedAt != null, cancellationToken),
            Payments: await db.Payments.CountAsync(p => p.ProjectId == projectId && p.VoidedAt == null, cancellationToken),
            PocketMoves: await db.PocketEntries.CountAsync(e => e.ProjectId == projectId, cancellationToken),
            SupervisionTaken: await db.SupervisionWithdrawals.CountAsync(w => w.ProjectId == projectId && w.DeletedAt == null, cancellationToken),
            Hours: await hours.CountAsync(cancellationToken),
            HoursInClosedMonths: await hours.CountAsync(e => e.StartedAt < openFromUtc, cancellationToken));
    }

    public async Task<IReadOnlyList<string>> RemovesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var removes = new List<string>();
        async Task Add<T>(IQueryable<T> rows, string one, string? many = null)
        {
            var n = await rows.CountAsync(cancellationToken);
            if (n > 0)
            {
                removes.Add(ProjectDeletionRules.Count(n, one, many));
            }
        }

        await Add(db.ProjectRequirements.Where(r => r.ProjectId == projectId), "requirement");
        await Add(db.Boqs.Where(b => b.ProjectId == projectId && b.DeletedAt == null), "BOQ or VO", "BOQs and VOs");
        await Add(db.DesignVos.Where(v => v.ProjectId == projectId), "variation order");
        await Add(db.ProjectPaymentChanges.Where(c => c.ProjectId == projectId), "price change");
        await Add(db.ProjectExpenses.Where(e => e.ProjectId == projectId && e.DeletedAt == null), "project expense");
        await Add(db.SiteExpenses.Where(e => e.ProjectId == projectId && e.DeletedAt == null), "site expense");
        await Add(db.LabourWeeks.Where(w => w.ProjectId == projectId), "week of labour", "weeks of labour");
        await Add(db.SiteLogs.Where(l => l.ProjectId == projectId), "site log");
        await Add(db.ProjectMeetings.Where(m => m.ProjectId == projectId && m.DeletedAt == null), "meeting");
        await Add(db.Invoices.Where(i => i.ProjectId == projectId && i.IssuedAt == null), "draft invoice");
        await Add(db.Payments.Where(p => p.ProjectId == projectId && p.VoidedAt != null), "voided payment");
        await Add(db.ProjectStaff.Where(s => s.ProjectId == projectId), "person on its site staff", "people on its site staff");
        await Add(db.ProjectDepartments.Where(d => d.ProjectId == projectId), "department share");
        await Add(db.ProjectBudgets.Where(b => b.ProjectId == projectId), "version of its cost budget", "versions of its cost budget");
        await Add(db.PortalItems.Where(i => i.ProjectId == projectId && i.RemovedAt == null), "client portal item");
        await Add(db.PortalMessages.Where(m => m.ProjectId == projectId && m.DeletedAt == null), "client portal message");
        await Add(db.PortalComments.Where(c => c.ProjectId == projectId && c.DeletedAt == null), "client portal comment");
        await Add(db.PortalFiles.Where(f => f.ProjectId == projectId), "stored file");
        await Add(db.PortalProjectAccess.Where(a => a.ProjectId == projectId), "client account's access to its portal", "client accounts' access to its portal");
        await Add(db.Notifications.Where(n => n.ProjectId == projectId), "notification");
        await Add(db.ActivityEvents.Where(e => e.ProjectId == projectId), "activity entry", "activity entries");
        await Add(db.WebsiteProjects.Where(w => w.ProjectId == projectId), "link from the public website (the page stays)", "links from the public website (the pages stay)");
        return removes;
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var keys = await db.PortalFiles.Where(f => f.ProjectId == projectId).Select(f => f.StorageKey).ToListAsync(cancellationToken);

        // the client portal: what points at its comments, messages, items and files before them
        var comments = db.PortalComments.Where(c => c.ProjectId == projectId).Select(c => c.Id);
        var messages = db.PortalMessages.Where(m => m.ProjectId == projectId).Select(m => m.Id);
        await db.PortalMentions
            .Where(m => (m.CommentId != null && comments.Contains(m.CommentId.Value)) || (m.MessageId != null && messages.Contains(m.MessageId.Value)))
            .ExecuteDeleteAsync(cancellationToken);
        await db.PortalCommentReads.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalReads.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalNotifications.Where(n => n.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectFileComments.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalComments.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalMessages.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalUploads.Where(u => u.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalHeroImages.Where(h => h.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalItems.Where(i => i.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalFiles.Where(f => f.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalInvitations.Where(i => i.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalProjectAccess.Where(a => a.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PortalSettings.Where(s => s.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        // what was said and pending about it
        await db.Notifications.Where(n => n.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ActivityEvents.Where(e => e.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.BlockedChanges.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.PocketRequests.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        // money that never moved: removed withdrawals, voided payments (before their invoices), draft invoices
        await db.SupervisionWithdrawals.Where(w => w.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.Payments.Where(p => p.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.Invoices.Where(i => i.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        // deleted hours entries: a chat message that shared one keeps its text
        var entries = db.WorkEntries.Where(e => e.ProjectId == projectId).Select(e => e.Id);
        await db.Messages.Where(m => m.RefEntryId != null && entries.Contains(m.RefEntryId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RefEntryId, (long?)null), cancellationToken);
        await db.WorkEntryComments.Where(c => entries.Contains(c.WorkEntryId)).ExecuteDeleteAsync(cancellationToken);
        await db.WorkEntries.Where(e => e.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        // the work and its costs; lines, days, assignees and attendees go with their rows
        await db.DesignVos.Where(v => v.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.Boqs.Where(b => b.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.LabourWeeks.Where(w => w.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectLabourDefaults.Where(d => d.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.SiteExpenses.Where(e => e.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectExpenses.Where(e => e.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.SiteLogs.Where(l => l.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectRequirements.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectMeetings.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectDepartmentChanges.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectDepartmentPlans.Where(p => p.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectDepartments.Where(d => d.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectBudgets.Where(b => b.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectStaff.Where(s => s.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectPaymentChanges.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        // the public website keeps its page, no longer tied to the project
        await db.WebsiteProjects.Where(w => w.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.ProjectId, (long?)null), cancellationToken);

        await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken);
        return keys;
    }
}
