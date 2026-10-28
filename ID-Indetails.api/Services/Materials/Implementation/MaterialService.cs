using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Materials;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Materials;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Materials.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Materials.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Materials.Implementation;

public sealed class MaterialService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IMaterialRepository materials,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IProfitFloor floor,
    IClock clock) : IMaterialService
{
    private const string ProjectNotFound = "Project not found.";
    private const string MaterialNotFound = "Material not found.";

    public async Task<MaterialListResponse> ListAsync(long projectId, MaterialListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution projects have materials.");
        }

        var all = await materials.ListAsync(projectId, cancellationToken);
        var matching = all.Where(r => Matches(r, query)).ToList();
        var page = matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var money = SeesMoney(actor);
        var open = IsOpen(project);

        return new MaterialListResponse(
            page.Select(r => ToResponse(r, project, actor, open)).ToList(),
            query.Page,
            query.PageSize,
            matching.Count,
            Totals(matching, project, money),
            Totals(all, project, money),
            all.GroupBy(r => MaterialRules.GroupKey(r.Material.Name))
                .Select(g => new MaterialTypeResponse(g.Key, g.First().Material.Name.Trim(), g.Count(), g.Sum(r => r.Material.Total)))
                .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            all.Where(r => !string.IsNullOrWhiteSpace(r.Material.Supplier))
                .GroupBy(r => MaterialRules.GroupKey(r.Material.Supplier!))
                .Select(g => new MaterialSupplierResponse(g.Key, g.First().Material.Supplier!.Trim()))
                .OrderBy(s => s.Key, StringComparer.Ordinal)
                .ToList(),
            all.GroupBy(r => r.Material.AddedById)
                .Select(g => new PersonRefResponse(g.Key, g.First().AddedByName))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            open && (actor.Has(PermissionCodes.MaterialCreate) || actor.Has(PermissionCodes.MaterialManage)),
            money ? project.MaterialMarkup : null);
    }

    public async Task<IReadOnlyList<MaterialResponse>> AddAsync(
        long projectId,
        AddMaterialsRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (request.Items.Any(i => i.ClientPrice is not null) && !actor.Has(PermissionCodes.ProjectPricingManage))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Setting a client price needs the project pricing permission.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedProjectAsync(actor, projectId, cancellationToken);
        var existing = await AlreadyAddedAsync(actor.UserId, project.Id, request.Items, cancellationToken);
        var fresh = request.Items.Where(i => i.ClientRef is not { } clientRef || !existing.ContainsKey(clientRef)).ToList();

        // A batch sent again whose materials were all added already changes nothing, the site's lock included.
        var added = new List<ProjectMaterial>();
        if (fresh.Count > 0)
        {
            Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
            var before = await floor.BeforeAsync(project, cancellationToken);
            var markup = project.MaterialMarkup ?? 0;
            foreach (var item in fresh)
            {
                var entry = MaterialRules.Normalise(new MaterialRules.Entry(item.Quantity, item.UnitCost, item.Amount));
                var material = new ProjectMaterial
                {
                    ProjectId = project.Id,
                    Currency = project.RevenueCurrency,
                    ClientPrice = MaterialRules.ManualPrice(item.ClientPrice, Cost(entry), markup),
                    AddedAt = clock.UtcNow,
                    AddedById = actor.UserId,
                    ClientRef = item.ClientRef,
                };
                Apply(material, item, entry);
                materials.Add(material);
                added.Add(material);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            foreach (var material in added)
            {
                activity.Add(MaterialEvent(project, material, ActivityEventKinds.MaterialAdded, actor.UserId, Cost(material)));
            }

            await NotifyAddedAsync(project, added.Count, actor.UserId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await floor.CheckAsync(before, BlockedChangeKind.MaterialAdd, new BlockedRequest<AddMaterialsRequest>(project.Id, request), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // In request order: each item's new material, or the one an earlier send of it already added.
        var newOnes = new Queue<ProjectMaterial>(added);
        var ids = request.Items
            .Select(i => i.ClientRef is { } clientRef && existing.TryGetValue(clientRef, out var earlier) ? earlier.Id : newOnes.Dequeue().Id)
            .ToList();
        var rows = await materials.GetRowsAsync(ids, cancellationToken);
        var open = IsOpen(project);
        return rows.Select(r => ToResponse(r, project, actor, open)).ToList();
    }

    public async Task<MaterialResponse> UpdateAsync(
        long projectId,
        long materialId,
        MaterialEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenProjectAsync(actor, projectId, cancellationToken);
        var material = await ChangeableMaterialAsync(actor, projectId, materialId, cancellationToken);

        var entry = MaterialRules.Normalise(new MaterialRules.Entry(request.Quantity, request.UnitCost, request.Amount));
        var costChanged = material.Name != request.Name.Trim()
            || material.Quantity != entry.Quantity
            || material.Unit != Trimmed(request.Unit)
            || material.UnitCost != entry.UnitCost
            || material.Amount != entry.Amount;
        if (costChanged)
        {
            Throw(MaterialRules.CheckNotStated(await materials.IsStatedAsync(material.Id, cancellationToken)));
        }

        var before = await floor.BeforeAsync(project, cancellationToken);
        var previousCost = Cost(material);
        Apply(material, request, entry);
        material.UpdatedAt = clock.UtcNow;
        activity.Add(MaterialEvent(project, material, ActivityEventKinds.MaterialUpdated, actor.UserId, Cost(material), previousCost));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await floor.CheckAsync(before, BlockedChangeKind.MaterialUpdate,
            new BlockedRequest<MaterialEntryRequest>(project.Id, request, ItemId: material.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(material.Id, project, actor, cancellationToken);
    }

    public async Task DeleteAsync(long projectId, long materialId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenProjectAsync(actor, projectId, cancellationToken);
        var material = await ChangeableMaterialAsync(actor, projectId, materialId, cancellationToken);
        Throw(MaterialRules.CheckNotStated(await materials.IsStatedAsync(material.Id, cancellationToken)));

        material.DeletedAt = clock.UtcNow;
        activity.Add(MaterialEvent(project, material, ActivityEventKinds.MaterialRemoved, actor.UserId, Cost(material)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MaterialResponse> SetPriceAsync(
        long projectId,
        long materialId,
        SetMaterialPriceRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenProjectAsync(actor, projectId, cancellationToken);
        var material = await materials.GetForUpdateAsync(projectId, materialId, cancellationToken)
            ?? throw new NotFoundException(MaterialNotFound);
        Throw(MaterialRules.CheckNotStated(await materials.IsStatedAsync(material.Id, cancellationToken)));

        var markup = project.MaterialMarkup ?? 0;
        var price = MaterialRules.ManualPrice(request.ClientPrice, Cost(material), markup);
        if (price != material.ClientPrice)
        {
            var previous = ClientPrice(material, markup);
            material.ClientPrice = price;
            material.UpdatedAt = clock.UtcNow;
            activity.Add(MaterialEvent(project, material, ActivityEventKinds.MaterialPriced, actor.UserId, ClientPrice(material, markup), previous));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(material.Id, project, actor, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>Locks the project row, so a change can't slip in while it is being finalised.</summary>
    private async Task<Project> OpenProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await LockedProjectAsync(actor, projectId, cancellationToken);
        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        return project;
    }

    /// <summary>The project, locked, if the caller can see it. Two sends of the same batch wait for each other here.</summary>
    private async Task<Project> LockedProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode) ? project : throw new NotFoundException(ProjectNotFound);
    }

    /// <summary>
    /// What the caller already added with the items' client refs, by ref. A ref already used on another site is refused:
    /// it can't be the same material.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, ProjectMaterial>> AlreadyAddedAsync(
        long actorId,
        long projectId,
        IReadOnlyList<NewMaterialRequest> items,
        CancellationToken cancellationToken)
    {
        var refs = items.Where(i => i.ClientRef is not null).Select(i => i.ClientRef!.Value).ToList();
        if (refs.Count == 0)
        {
            return new Dictionary<Guid, ProjectMaterial>();
        }

        var found = await materials.ByClientRefsAsync(actorId, refs, cancellationToken);
        if (found.FirstOrDefault(m => m.ProjectId != projectId) is { } elsewhere)
        {
            var index = items.ToList().FindIndex(i => i.ClientRef == elsewhere.ClientRef);
            throw new RequestValidationException(
                ErrorCodes.ValidationFailed, $"items[{index}].clientRef", "This client reference was already used for a material on another site.");
        }

        return found.ToDictionary(m => m.ClientRef!.Value);
    }

    private async Task<ProjectMaterial> ChangeableMaterialAsync(UserAccess actor, long projectId, long materialId, CancellationToken cancellationToken)
    {
        var material = await materials.GetForUpdateAsync(projectId, materialId, cancellationToken)
            ?? throw new NotFoundException(MaterialNotFound);
        Throw(SiteRules.CheckCanChange(material.AddedById == actor.UserId, actor.Has(PermissionCodes.MaterialManage)));
        return material;
    }

    private async Task NotifyAddedAsync(Project project, int count, long actorId, CancellationToken cancellationToken)
        => await notifications.SendAsync(
            new NotificationDraft(
                NotificationKinds.MaterialsAdded,
                actorId,
                project.DomainCode,
                project.Id,
                "project",
                project.Id,
                new { count, projectTitle = project.Title }),
            await notifications.HoldersAsync(project.DomainCode, PermissionCodes.MaterialManage, cancellationToken),
            cancellationToken);

    private async Task<MaterialResponse> DescribeAsync(long materialId, Project project, UserAccess actor, CancellationToken cancellationToken)
        => ToResponse((await materials.GetRowsAsync([materialId], cancellationToken))[0], project, actor, open: true);

    private bool Matches(MaterialRecord row, MaterialListQuery query)
    {
        var m = row.Material;
        if (query.Type is { Length: > 0 } type && MaterialRules.GroupKey(m.Name) != type)
        {
            return false;
        }

        if (query.Supplier is { Length: > 0 } supplier && (m.Supplier is null || MaterialRules.GroupKey(m.Supplier) != supplier))
        {
            return false;
        }

        if (query.AddedById is { } addedBy && m.AddedById != addedBy)
        {
            return false;
        }

        var day = clock.ToCompanyDate(m.AddedAt);
        if (day < query.From || day > query.To)
        {
            return false;
        }

        var search = query.Search?.Trim();
        return string.IsNullOrEmpty(search)
            || new[] { m.Name, m.Supplier, m.Note, m.Unit, row.AddedByName }
                .Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    private MaterialResponse ToResponse(MaterialRecord row, Project project, UserAccess actor, bool open)
    {
        var m = row.Material;
        var money = SeesMoney(actor);
        var markup = project.MaterialMarkup ?? 0;
        var price = ClientPrice(m, markup);
        var canWrite = actor.Has(PermissionCodes.MaterialCreate) || actor.Has(PermissionCodes.MaterialManage);

        return new MaterialResponse
        {
            Id = m.Id,
            Name = m.Name,
            Quantity = m.Quantity,
            Unit = m.Unit,
            UnitCost = m.UnitCost,
            Cost = Cost(m),
            Currency = m.Currency,
            Supplier = m.Supplier,
            Note = m.Note,
            AddedAt = m.AddedAt,
            AddedOn = clock.ToCompanyDate(m.AddedAt),
            AddedBy = new PersonRefResponse(m.AddedById, row.AddedByName),
            UpdatedAt = m.UpdatedAt,
            IsStated = row.IsStated,
            SentOn = row.SentAt is { } sent ? clock.ToCompanyDate(sent) : null,
            CanEdit = open && canWrite && SiteRules.CanChange(m.AddedById == actor.UserId, actor.Has(PermissionCodes.MaterialManage)),
            ClientPrice = money ? price : null,
            PriceSource = money ? MaterialRules.SourceOf(m.ClientPrice, row.IsStated) : null,
            Markup = money ? Math.Round(price / Cost(m) - 1, 4, MidpointRounding.AwayFromZero) : null,
        };
    }

    private static MaterialTotalsResponse Totals(IReadOnlyList<MaterialRecord> rows, Project project, bool money)
        => new(
            rows.Count,
            rows.Sum(r => Cost(r.Material)),
            money ? rows.Sum(r => ClientPrice(r.Material, project.MaterialMarkup ?? 0)) : null);

    private static bool SeesMoney(UserAccess actor)
        => actor.Has(PermissionCodes.FinancialView) || actor.Has(PermissionCodes.ProjectPricingManage);

    private static bool IsOpen(Project project)
        => SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null) is null;

    private static void Apply(ProjectMaterial material, MaterialEntryRequest request, MaterialRules.Entry entry)
    {
        material.Name = request.Name.Trim();
        material.Quantity = entry.Quantity;
        material.Unit = Trimmed(request.Unit);
        material.UnitCost = entry.UnitCost;
        material.Amount = entry.Amount;
        material.Supplier = Trimmed(request.Supplier);
        material.Note = Trimmed(request.Note);
    }

    /// <summary>Worked out here rather than read from the generated column, which is only filled in after a save.</summary>
    private static decimal Cost(ProjectMaterial m) => MaterialRules.Cost(m.Quantity, m.UnitCost, m.Amount);

    private static decimal Cost(MaterialRules.Entry e) => MaterialRules.Cost(e.Quantity, e.UnitCost, e.Amount);

    private static decimal ClientPrice(ProjectMaterial m, decimal markup)
        => ExecutionPricing.ClientPrice(new ExecutionPricing.Material(Cost(m), m.ClientPrice), markup);

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private ActivityEvent MaterialEvent(Project project, ProjectMaterial material, string kind, long actorId, decimal amount, decimal? prevAmount = null) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "material",
        SubjectId = material.Id,
        Amount = amount,
        PrevAmount = prevAmount,
        Payload = JsonSerializer.Serialize(new { name = material.Name }),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
