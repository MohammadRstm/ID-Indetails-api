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

namespace ID_Indetails.api.Services.Materials.Implementation;

public sealed class MaterialService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IMaterialRepository materials,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
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
        var project = await OpenProjectAsync(actor, projectId, cancellationToken);
        var markup = project.MaterialMarkup ?? 0;

        var added = request.Items.Select(item =>
        {
            var entry = MaterialRules.Normalise(new MaterialRules.Entry(item.Quantity, item.UnitCost, item.Amount));
            var material = new ProjectMaterial
            {
                ProjectId = project.Id,
                Currency = project.RevenueCurrency,
                ClientPrice = MaterialRules.ManualPrice(item.ClientPrice, Cost(entry), markup),
                AddedAt = clock.UtcNow,
                AddedById = actor.UserId,
            };
            Apply(material, item, entry);
            materials.Add(material);
            return material;
        }).ToList();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var material in added)
        {
            activity.Add(MaterialEvent(project, material, ActivityEventKinds.MaterialAdded, actor.UserId, Cost(material)));
        }

        await NotifyAddedAsync(project, added.Count, actor.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var rows = await materials.GetRowsAsync(added.Select(m => m.Id).ToList(), cancellationToken);
        return rows.Select(r => ToResponse(r, project, actor, open: true)).ToList();
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

        var previousCost = Cost(material);
        Apply(material, request, entry);
        material.UpdatedAt = clock.UtcNow;
        activity.Add(MaterialEvent(project, material, ActivityEventKinds.MaterialUpdated, actor.UserId, Cost(material), previousCost));
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        return project;
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
