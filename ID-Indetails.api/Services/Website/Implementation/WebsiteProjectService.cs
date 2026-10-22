using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Website;
using ID_Indetails.api.DTO.Response.Website;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Website.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Geocoding.Abstraction;
using ID_Indetails.api.Services.Website.Abstraction;

namespace ID_Indetails.api.Services.Website.Implementation;

/// <summary>
/// The website's projects (<see cref="WebsiteRules"/>). A project linked to a system project follows its status and
/// year until they are typed in here. Its place is worked out by <see cref="WebsitePlaceResolver"/>. Only published projects reach the website, and only they can be in the moving
/// strip, which holds at most 20.
/// </summary>
public sealed class WebsiteProjectService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IWebsiteRepository website,
    IFileClaims fileClaims,
    WebsitePlaceResolver places,
    IPlaceDirectory directory,
    IUnitOfWork unitOfWork,
    IClock clock) : IWebsiteProjectService
{
    private const string NotFound = "Website project not found.";
    private const int CandidateCount = 20;

    public async Task<WebsiteProjectsResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = await website.ProjectsAsync(publishedOnly: false, cancellationToken);
        var linked = await website.SystemProjectsAsync(LinkIds(projects), cancellationToken);
        var canManage = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Has(PermissionCodes.WebsiteManage);
        return new WebsiteProjectsResponse(projects.Select(p => Describe(p, linked)).ToList(), WebsiteRules.MaxConveyor, canManage);
    }

    public async Task<WebsiteProjectResponse> GetAsync(long id, CancellationToken cancellationToken = default)
        => await DescribeAsync(await website.ProjectAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound), cancellationToken);

    public async Task<WebsiteProjectResponse> CreateAsync(WebsiteProjectRequest request, CancellationToken cancellationToken = default)
        => await DescribeAsync(await SaveAsync(null, request, cancellationToken), cancellationToken);

    public async Task<WebsiteProjectResponse> UpdateAsync(long id, WebsiteProjectRequest request, CancellationToken cancellationToken = default)
        => await DescribeAsync(await SaveAsync(id, request, cancellationToken), cancellationToken);

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var project = await website.ProjectAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound);
        if (project.ConveyorOrder is not null)
        {
            await LeaveConveyorAsync(project, cancellationToken);
        }

        website.Remove(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebsiteProjectsResponse> SetConveyorAsync(WebsiteConveyorRequest request, CancellationToken cancellationToken = default)
    {
        var strip = await website.ConveyorAsync(cancellationToken);
        var chosen = (await website.ProjectsByIdAsync(request.ProjectIds, cancellationToken)).ToDictionary(p => p.Id);
        if (chosen.Count != request.ProjectIds.Count)
        {
            throw new NotFoundException("A project chosen for the moving strip no longer exists.");
        }

        if (chosen.Values.FirstOrDefault(p => !p.Published) is { } draft)
        {
            throw new BusinessRuleException(
                ErrorCodes.WebsiteConveyorInvalid, $"\"{draft.Title}\" isn't published, so it can't be in the moving strip.");
        }

        foreach (var project in strip)
        {
            project.ConveyorOrder = null;
        }

        for (var index = 0; index < request.ProjectIds.Count; index++)
        {
            chosen[request.ProjectIds[index]].ConveyorOrder = index + 1;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    public async Task<WebsiteProjectResponse> AddImageAsync(long id, WebsiteImageRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await website.ProjectAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound);
        var siblings = project.Images.Where(i => i.Concept == request.Concept).ToList();
        if (request.Concept && siblings.Count >= WebsiteRules.MaxConceptImages)
        {
            throw new BusinessRuleException(ErrorCodes.WebsiteConceptFull, $"A project has at most {WebsiteRules.MaxConceptImages} concept pictures. Remove one first.");
        }

        var file = await fileClaims.ClaimWebsitePictureAsync(request.FileId, cancellationToken);
        var image = new WebsiteProjectImage
        {
            WebsiteProjectId = project.Id,
            FileId = file.Id,
            Concept = request.Concept,
            Position = siblings.Select(i => i.Position).DefaultIfEmpty(0).Max() + 1,
        };
        image.Alt = request.Alt.Trim();
        project.Images.Add(image);
        Touch(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<WebsiteProjectResponse> UpdateImageAsync(
        long id, long imageId, WebsiteImageEditRequest request, CancellationToken cancellationToken = default)
    {
        var project = await website.ProjectAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound);
        ImageOf(project, imageId).Alt = request.Alt.Trim();
        Touch(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(project, cancellationToken);
    }

    /// <summary>The picture is attached to nothing any more, so the file cleanup deletes it.</summary>
    public async Task<WebsiteProjectResponse> RemoveImageAsync(long id, long imageId, CancellationToken cancellationToken = default)
    {
        var project = await website.ProjectAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound);
        var image = ImageOf(project, imageId);
        project.Images.Remove(image);
        website.Remove(image);
        Touch(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(project, cancellationToken);
    }

    public async Task<WebsiteProjectResponse> OrderImagesAsync(long id, WebsiteOrderRequest request, CancellationToken cancellationToken = default)
    {
        var project = await website.ProjectAsync(id, cancellationToken) ?? throw new NotFoundException(NotFound);
        // the gallery and the concept pictures are ordered apart: the first id says which is being ordered
        var concept = project.Images.FirstOrDefault(i => request.Ids.Count > 0 && i.Id == request.Ids[0])?.Concept ?? false;
        if (!WebsiteRules.IsWholeOrder(project.Images.Where(i => i.Concept == concept).Select(i => i.Id), request.Ids))
        {
            throw new ConflictException(ErrorCodes.WebsiteOrderStale, "The pictures changed since you looked: one was added or removed. Your new order wasn't saved.");
        }

        var images = project.Images.ToDictionary(i => i.Id);
        for (var index = 0; index < request.Ids.Count; index++)
        {
            images[request.Ids[index]].Position = index + 1;
        }

        Touch(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(project, cancellationToken);
    }

    /// <summary>Only projects of the businesses they can see: the website's own staff type theirs in.</summary>
    public async Task<IReadOnlyList<WebsiteLinkCandidateResponse>> CandidatesAsync(string? search, CancellationToken cancellationToken = default)
        => (await website.CandidatesAsync(
                (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).VisibleBusinesses, search, CandidateCount, cancellationToken))
            .Select(p => new WebsiteLinkCandidateResponse(
                p.Id, p.Title, p.DomainCode, WebsiteRules.StatusOf(p.Status), YearOf(p), p.ClientName, p.Description, p.WebsiteProjectId))
            .ToList();

    private async Task<WebsiteProject> SaveAsync(long? id, WebsiteProjectRequest request, CancellationToken cancellationToken)
    {
        var project = id is { } existing
            ? await website.ProjectAsync(existing, cancellationToken) ?? throw new NotFoundException(NotFound)
            : new WebsiteProject { CreatedAt = clock.UtcNow, CreatedById = currentUser.Id };

        // the map lookups come first, outside the transaction: they can take seconds
        var place = await places.ResolveAsync(
            request.City, request.Country, request.Latitude, request.Longitude,
            id is null ? null : new StoredPlace(project.City, project.Country, project.Latitude, project.Longitude, project.PositionApproximate),
            new PlaceOwner(id, null), cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        SystemProjectFacts? linked = null;
        if (request.ProjectId is { } projectId)
        {
            linked = (await website.SystemProjectsAsync([projectId], cancellationToken)).GetValueOrDefault(projectId)
                ?? throw new NotFoundException("That project doesn't exist.");
            if (linked.WebsiteProjectId is { } shownBy && shownBy != project.Id)
            {
                throw new ConflictException(ErrorCodes.WebsiteProjectAlreadyLinked, $"\"{linked.Title}\" is already on the website as another project.");
            }
        }

        project.Slug = await SlugAsync(project, request.Title.Trim(), cancellationToken);
        project.Title = request.Title.Trim();
        project.Type = await TypeAsync(project, request.Type, cancellationToken);
        project.Status = request.Status;
        project.Year = request.Year;
        project.Client = Clean(request.Client);
        project.City = place.City;
        project.Country = place.Country;
        project.Latitude = place.Latitude;
        project.Longitude = place.Longitude;
        project.PositionApproximate = place.Approximate;
        project.Description = Clean(request.Description);
        project.ConceptDescription = Clean(request.ConceptDescription);
        project.ProjectId = request.ProjectId;
        project.ThumbnailFileId = await PictureAsync(project.ThumbnailFileId, request.ThumbnailFileId, cancellationToken);
        project.HeroFileId = await PictureAsync(project.HeroFileId, request.HeroFileId, cancellationToken);
        project.HeroAlt = Clean(request.HeroAlt);
        project.Published = request.Published;

        if (project.Published)
        {
            if (place.Problem is not null)
            {
                throw new BusinessRuleException(ErrorCodes.WebsitePlaceNotFound, place.Problem);
            }

            var missing = MissingToPublish(project, linked);
            if (missing.Count > 0)
            {
                throw new BusinessRuleException(
                    ErrorCodes.WebsiteProjectIncomplete, $"Before it can be published, the project needs {string.Join(", ", missing)}.");
            }
        }
        else if (project.ConveyorOrder is not null)
        {
            await LeaveConveyorAsync(project, cancellationToken);
        }

        if (id is null)
        {
            website.Add(project);
        }
        else
        {
            Touch(project);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return project;
    }

    /// <summary>
    /// The type as typed and tidied, or the one another project already has with the same key, so "villa" and "Villa"
    /// stay one type on the website.
    /// </summary>
    private async Task<string> TypeAsync(WebsiteProject project, string typed, CancellationToken cancellationToken)
    {
        var type = WebsiteRules.CleanType(typed);
        var key = WebsiteRules.TypeKey(type);
        return (await website.ProjectTypesAsync(project.Id, cancellationToken)).FirstOrDefault(t => WebsiteRules.TypeKey(t) == key) ?? type;
    }

    /// <summary>
    /// The page address from the name: kept while the name makes the same one, otherwise the next free one
    /// ("villa-rizk", then "villa-rizk-2").
    /// </summary>
    private async Task<string> SlugAsync(WebsiteProject project, string title, CancellationToken cancellationToken)
    {
        long? self = project.Id == 0 ? null : project.Id;
        var start = WebsiteRules.SlugOf(title);
        if (project.Slug.Length > 0 && WebsiteRules.SlugOf(project.Title) == start)
        {
            return project.Slug;
        }

        var candidate = start;
        for (var n = 2; await website.SlugTakenAsync(candidate, self, cancellationToken); n++)
        {
            var suffix = $"-{n}";
            candidate = start[..Math.Min(start.Length, WebsiteRules.MaxSlugLength - suffix.Length)].TrimEnd('-') + suffix;
        }

        return candidate;
    }

    /// <summary>The picture kept, cleared, or newly attached from the caller's upload.</summary>
    private async Task<long?> PictureAsync(long? current, long? requested, CancellationToken cancellationToken)
        => requested is null || requested == current
            ? requested
            : (await fileClaims.ClaimWebsitePictureAsync(requested.Value, cancellationToken)).Id;

    /// <summary>Takes the project out of the moving strip and closes the gap.</summary>
    private async Task LeaveConveyorAsync(WebsiteProject project, CancellationToken cancellationToken)
    {
        var rest = (await website.ConveyorAsync(cancellationToken)).Where(p => p.Id != project.Id).ToList();
        project.ConveyorOrder = null;
        for (var index = 0; index < rest.Count; index++)
        {
            rest[index].ConveyorOrder = index + 1;
        }
    }

    private void Touch(WebsiteProject project)
    {
        project.UpdatedAt = clock.UtcNow;
        project.UpdatedById = currentUser.Id;
    }

    private async Task<WebsiteProjectResponse> DescribeAsync(WebsiteProject project, CancellationToken cancellationToken)
        => Describe(project, await website.SystemProjectsAsync(LinkIds([project]), cancellationToken));

    private WebsiteProjectResponse Describe(WebsiteProject p, IReadOnlyDictionary<long, SystemProjectFacts> linked)
    {
        var link = p.ProjectId is { } id ? linked.GetValueOrDefault(id) : null;
        var (status, year) = Shown(p, link);
        return new WebsiteProjectResponse(
            p.Id, p.Slug, p.Title, p.Type, p.Status, p.Year, status, year, p.Client, p.City, p.Country, CodeOf(p.Country), p.Latitude, p.Longitude, p.PositionApproximate,
            p.Description, p.ThumbnailFileId, p.HeroFileId, p.HeroAlt,
            link is null ? null : new WebsiteLinkedProjectResponse(link.Id, link.Title, link.DomainCode, WebsiteRules.StatusOf(link.Status), YearOf(link)),
            p.Published, p.ConveyorOrder,
            p.Images.Where(i => !i.Concept).OrderBy(i => i.Position).Select(i => new WebsiteImageResponse(i.Id, i.FileId, i.Alt)).ToList(),
            p.ConceptDescription,
            p.Images.Where(i => i.Concept).OrderBy(i => i.Position).Select(i => new WebsiteImageResponse(i.Id, i.FileId, i.Alt)).ToList(),
            MissingToPublish(p, link), p.CreatedAt, p.UpdatedAt);
    }

    /// <summary>The status and year the website shows: the project's own, else the linked project's.</summary>
    internal static (WebsiteProjectStatus? Status, int? Year) Shown(WebsiteProject project, SystemProjectFacts? link)
        => (project.Status ?? (link is null ? null : WebsiteRules.StatusOf(link.Status)), project.Year ?? (link is null ? null : YearOf(link)));

    private string? CodeOf(string? country) => country is null ? null : directory.Country(country)?.Code;

    private static IReadOnlyList<string> MissingToPublish(WebsiteProject p, SystemProjectFacts? link)
    {
        var (status, year) = Shown(p, link);
        return WebsiteRules.MissingToPublish(status, year, p.Country, p.Latitude, p.Longitude, p.Description, p.ThumbnailFileId, p.HeroFileId);
    }

    internal static int YearOf(SystemProjectFacts p) => WebsiteRules.YearOf(p.CompletedAt, p.TargetDate, p.StartDate);

    private static IReadOnlyCollection<long> LinkIds(IEnumerable<WebsiteProject> projects)
        => projects.Where(p => p.ProjectId is not null).Select(p => p.ProjectId!.Value).ToList();

    private static WebsiteProjectImage ImageOf(WebsiteProject project, long imageId)
        => project.Images.SingleOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("Picture not found.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
