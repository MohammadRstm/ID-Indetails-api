using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// The draft and the published copy of a project's client portal: the files chosen for the client (chosen in Project
/// files), the hero images and the introduction. Holders of client_portal:manage or client_access:manage look at it;
/// only client_portal:manage changes it. Every change takes the project's content lock and moves the revision on.
/// Publishing copies the chosen files' drafts, the hero images and the introduction into the published copy in one
/// transaction: clients see the old version or the new one, never a mix, and a publish naming an older revision than
/// the current one is refused rather than publishing what the person didn't see.
/// </summary>
public sealed class PortalPublishingService(
    ICurrentUser currentUser,
    IPortalContentViews views,
    IPortalContentRepository content,
    IPortalContentMaintenance maintenance,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IPortalPublishingService
{
    private const string ItemNotFound = "File not found.";

    /// <summary>What a draft change works on, tracked under the content lock: the settings, every item and every hero image row.</summary>
    private sealed record Draft(PortalSettings Settings, IReadOnlyList<PortalItem> Items, List<PortalHeroImage> Heroes);

    public async Task<PortalContentResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
        => await views.ContentAsync(await views.VisibleProjectAsync(projectId, cancellationToken), cancellationToken);

    public async Task<PortalContentLinkResponse> LinkAsync(long projectId, long itemId, bool inline, CancellationToken cancellationToken = default)
    {
        await views.VisibleProjectAsync(projectId, cancellationToken);
        var item = await content.ItemViewAsync(projectId, itemId, cancellationToken);
        if (item is null || !PortalPublishRules.IsClientRelevant(item.ForClient, item.PublishedFileId))
        {
            // the team's own files are reached through Project files
            throw new NotFoundException(ItemNotFound);
        }

        return await views.LinkAsync(item.File, inline, cancellationToken);
    }

    public Task<PortalContentResponse> AddHeroAsync(long projectId, PortalHeroRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, draft =>
        {
            var item = draft.Items.SingleOrDefault(i => i.Id == request.ItemId && i.RemovedAt is null) ?? throw new NotFoundException(ItemNotFound);
            if (!PortalPublishRules.CanBeHero(item.File.ContentType))
            {
                throw new RequestValidationException(ErrorCodes.PortalHeroNotImage, "itemId", "Only a JPEG, PNG or WebP image can be a hero image.");
            }

            var heroes = InOrder(draft.Heroes, published: false);
            if (heroes.Any(h => h.FileId == item.File.Id))
            {
                throw new ConflictException(ErrorCodes.PortalHeroAlreadyAdded, "This image is already one of the hero images.");
            }

            if (PortalPublishRules.CheckCanAddHero(heroes.Count) is { } full)
            {
                throw full.ToException();
            }

            var hero = new PortalHeroImage
            {
                ProjectId = projectId,
                FileId = item.File.Id,
                File = item.File,
                SortOrder = PortalContentRules.NextSortOrder(heroes.Max(h => (int?)h.SortOrder)),
                IsPublished = false,
            };
            content.Add(hero);
            draft.Heroes.Add(hero);
            return [];
        }, cancellationToken);

    /// <summary>The file stays while anything else uses it: an item, or the published hero images.</summary>
    public Task<PortalContentResponse> RemoveHeroAsync(long projectId, long fileId, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, draft =>
        {
            var hero = draft.Heroes.SingleOrDefault(h => !h.IsPublished && h.FileId == fileId) ?? throw new NotFoundException("Hero image not found.");
            content.Remove(hero);
            draft.Heroes.Remove(hero);
            return [hero.File];
        }, cancellationToken);

    public Task<PortalContentResponse> OrderHeroesAsync(long projectId, PortalHeroOrderRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, draft =>
        {
            var current = InOrder(draft.Heroes, published: false).ToDictionary(h => h.FileId);
            if (!PortalPublishRules.IsWholeOrder(current.Keys, request.FileIds))
            {
                throw new ConflictException(
                    ErrorCodes.PortalOrderStale, "The hero images changed since you looked: one was added or removed. Your new order wasn't saved.");
            }

            for (var index = 0; index < request.FileIds.Count; index++)
            {
                current[request.FileIds[index]].SortOrder = PortalPublishRules.SortOrderAt(index);
            }

            return [];
        }, cancellationToken);

    public Task<PortalContentResponse> SetSummaryAsync(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken = default)
        => ChangeAsync(projectId, draft =>
        {
            draft.Settings.Summary = PortalPublishRules.CleanSummary(request.Summary);
            return [];
        }, cancellationToken);

    public async Task<PortalContentResponse> PublishAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default)
    {
        var project = await EditableProjectAsync(projectId, cancellationToken);
        var now = clock.UtcNow;
        List<PortalUpload> cancelled = [];
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            RequireRevision(settings, request.ExpectedRevision);
            var items = (await content.ItemsToChangeAsync(projectId, cancellationToken)).ToList();
            var heroes = (await content.HeroesToChangeAsync(projectId, cancellationToken)).ToList();
            var changes = PortalPublishRules.ChangesOf(
                items.Select(Copies).ToList(), FileIds(heroes, published: false), FileIds(heroes, published: true),
                settings.Summary, settings.PublishedSummary);
            if (!changes.Any)
            {
                // nothing would change for clients
                return await views.ContentAsync(project, cancellationToken);
            }

            var candidates = new List<PortalFile>();
            var oldPublished = await content.FilesAsync(items.Where(i => i.PublishedFileId is { } p && p != i.FileId).Select(i => i.PublishedFileId!.Value), cancellationToken);
            candidates.AddRange(oldPublished);

            // deleted from Project files: gone for clients too now
            foreach (var item in items.Where(i => i.RemovedAt is not null).ToList())
            {
                cancelled.AddRange(await StopReplacementsAsync(item.Id, now, cancellationToken));
                candidates.Add(item.File);
                content.Remove(item);
                items.Remove(item);
            }

            foreach (var item in items)
            {
                if (item.ForClient)
                {
                    item.PublishedFileId = item.FileId;
                    item.PublishedTitle = item.Title;
                    item.PublishedSection = item.Section;
                    item.PublishedSortOrder = item.SortOrder;
                }
                else if (item.PublishedFileId is not null)
                {
                    // no longer chosen: clients stop seeing it, and it stays one of the project's files
                    item.PublishedFileId = null;
                    item.PublishedTitle = null;
                    item.PublishedSection = null;
                    item.PublishedSortOrder = null;
                }
            }

            candidates.AddRange(CopyHeroes(heroes, toPublished: true));
            settings.PublishedSummary = settings.Summary;
            settings.PublishedRevision = settings.Revision;
            settings.PublishedAt = now;
            settings.PublishedById = currentUser.Id;
            released = PortalReferences.Release(candidates, items, heroes, now);

            // one event per publication: what later notifications to clients hook into
            activity.Add(new ActivityEvent
            {
                DomainCode = project.DomainCode,
                Kind = ActivityEventKinds.PortalPublished,
                OccurredAt = now,
                ActorId = currentUser.Id,
                ProjectId = project.Id,
                ClientId = project.ClientId,
                Payload = JsonSerializer.Serialize(new { added = changes.Added, updated = changes.Updated, removed = changes.Removed, revision = settings.Revision }),
            });

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var upload in cancelled)
        {
            await maintenance.RemoveUploadAsync(upload, cancellationToken);
        }

        await maintenance.DeleteReleasedAsync(released, cancellationToken);
        return await views.ContentAsync(project, cancellationToken);
    }

    /// <summary>
    /// Only the client's side goes back: each file is chosen again exactly when clients see it, and the hero images and
    /// introduction are the published ones. Titles, sections, order, replaced files and deletions belong to Project
    /// files and stay as they are.
    /// </summary>
    public async Task<PortalContentResponse> DiscardAsync(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken = default)
    {
        var project = await EditableProjectAsync(projectId, cancellationToken);
        var now = clock.UtcNow;
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            RequireRevision(settings, request.ExpectedRevision);
            if (settings.PublishedAt is null)
            {
                throw new BusinessRuleException(
                    ErrorCodes.PortalNeverPublished, "Nothing has been published yet, so there's no version to go back to.");
            }

            var items = await content.ItemsToChangeAsync(projectId, cancellationToken);
            var heroes = (await content.HeroesToChangeAsync(projectId, cancellationToken)).ToList();
            foreach (var item in items.Where(i => i.RemovedAt is null))
            {
                item.ForClient = item.PublishedFileId is not null;
            }

            var candidates = CopyHeroes(heroes, toPublished: false);
            settings.Summary = settings.PublishedSummary;
            settings.Revision++;
            released = PortalReferences.Release(candidates, items, heroes, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await maintenance.DeleteReleasedAsync(released, cancellationToken);
        return await views.ContentAsync(project, cancellationToken);
    }

    public async Task<PortalPresentationResponse> PreviewAsync(long projectId, CancellationToken cancellationToken = default)
        => await views.PresentationAsync(await views.VisibleProjectAsync(projectId, cancellationToken), published: false, cancellationToken);

    /// <summary>
    /// One draft change under the project's content lock: <paramref name="change"/> edits the draft and returns the
    /// files it may have let go; the revision moves on, and files nothing points at any more are deleted.
    /// </summary>
    private async Task<PortalContentResponse> ChangeAsync(
        long projectId, Func<Draft, IEnumerable<PortalFile>> change, CancellationToken cancellationToken)
    {
        var project = await EditableProjectAsync(projectId, cancellationToken);
        var now = clock.UtcNow;
        List<PortalFile> released;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var settings = await content.LockSettingsAsync(projectId, cancellationToken);
            var items = await content.ItemsToChangeAsync(projectId, cancellationToken);
            var heroes = (await content.HeroesToChangeAsync(projectId, cancellationToken)).ToList();
            var candidates = change(new Draft(settings, items, heroes));
            settings.Revision++;
            released = PortalReferences.Release(candidates, items, heroes, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await maintenance.DeleteReleasedAsync(released, cancellationToken);
        return await views.ContentAsync(project, cancellationToken);
    }

    /// <summary>A Design client project, not archived, whose client portal the caller manages (client_portal:manage).</summary>
    private async Task<PortalContentProject> EditableProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var project = await views.ManagedProjectAsync(projectId, cancellationToken);
        if (PortalContentRules.CheckEditable(project.BillingType, project.ClientId, project.Status) is { } denial)
        {
            throw denial.ToException();
        }

        return project;
    }

    private static void RequireRevision(PortalSettings settings, int expected)
    {
        if (settings.Revision != expected)
        {
            throw new ConflictException(
                ErrorCodes.PortalRevisionStale, "The drafts changed since you looked. Check what's new, then try again.");
        }
    }

    /// <summary>
    /// Makes one list of hero images (the published one, or the draft) the same images in the same order as the other.
    /// Rows whose image stays are kept and only take their new place, so an image is never in a list twice; the files of
    /// the rows taken away are returned, to be let go if nothing else uses them.
    /// </summary>
    private List<PortalFile> CopyHeroes(List<PortalHeroImage> heroes, bool toPublished)
    {
        var source = InOrder(heroes, published: !toPublished);
        var target = InOrder(heroes, published: toPublished).ToDictionary(h => h.FileId);
        var dropped = new List<PortalFile>();
        foreach (var stale in target.Values.Where(t => source.All(s => s.FileId != t.FileId)))
        {
            content.Remove(stale);
            heroes.Remove(stale);
            dropped.Add(stale.File);
        }

        foreach (var from in source)
        {
            if (target.TryGetValue(from.FileId, out var kept))
            {
                kept.SortOrder = from.SortOrder;
            }
            else
            {
                var copy = new PortalHeroImage
                {
                    ProjectId = from.ProjectId,
                    FileId = from.FileId,
                    File = from.File,
                    SortOrder = from.SortOrder,
                    IsPublished = toPublished,
                };
                content.Add(copy);
                heroes.Add(copy);
            }
        }

        return dropped;
    }

    /// <summary>The draft (or published) hero image rows, in order.</summary>
    private static List<PortalHeroImage> InOrder(IEnumerable<PortalHeroImage> heroes, bool published)
        => heroes.Where(h => h.IsPublished == published).OrderBy(h => h.SortOrder).ThenBy(h => h.Id).ToList();

    private static IReadOnlyList<long> FileIds(IEnumerable<PortalHeroImage> heroes, bool published)
        => InOrder(heroes, published).Select(h => h.FileId).ToList();

    /// <summary>Uploads on their way to replace the item's file are cancelled, and none of them points at it any more.</summary>
    private async Task<List<PortalUpload>> StopReplacementsAsync(long itemId, DateTime now, CancellationToken cancellationToken)
    {
        var cancelled = new List<PortalUpload>();
        foreach (var upload in await content.UploadsReplacingAsync(itemId, cancellationToken))
        {
            if (upload.Status == PortalUploadStatus.Uploading)
            {
                upload.Status = PortalUploadStatus.Cancelled;
                upload.LastActivityAt = now;
                cancelled.Add(upload);
            }

            upload.ReplaceItemId = null;
        }

        return cancelled;
    }

    private static PortalItemCopies Copies(PortalItem i)
        => new(i.Id, i.FileId, i.Title, i.Section, i.SortOrder, i.PublishedFileId, i.PublishedTitle, i.PublishedSection, i.PublishedSortOrder,
            i.RemovedAt is not null, i.ForClient);
}
