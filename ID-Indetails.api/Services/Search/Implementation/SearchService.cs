using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Search;
using ID_Indetails.api.DTO.Response.Search;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Search.Abstraction;

namespace ID_Indetails.api.Services.Search.Implementation;

/// <summary>
/// The command palette, as in the prototype: projects, then clients, then people, 12 results in all, each group found
/// with its own list's search and visibility. A group the caller may not list (<c>project:view</c>, <c>client:view</c>,
/// <c>employee:view</c>) is left empty, so employees find projects only.
/// </summary>
public sealed class SearchService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IClientRepository clients,
    IEmployeeRepository employees) : ISearchService
{
    public const int MaxResults = 12;

    public async Task<NavCountsResponse> CountsAsync(NavCountsQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var domain = query.Domain;

        // each as its list counts it with the sidebar's filters (GET /projects?status=active, /clients, /employees?status=active)
        int? projectCount = actor.Has(PermissionCodes.ProjectView)
            ? await projects.CountAsync(new ProjectSearch(actor.VisibleDomains, null, ProjectStatus.Active, null, null, domain, 1, 1), cancellationToken)
            : null;
        int? clientCount = actor.Has(PermissionCodes.ClientView)
            ? (await clients.SearchAsync(new ClientSearch(actor.VisibleDomains, null, null, domain, 1, 0), cancellationToken)).TotalCount
            : null;
        int? peopleCount = actor.Has(PermissionCodes.EmployeeView)
            ? (await employees.SearchAsync(
                new EmployeeSearch(actor.VisibleDomains, EmployeeRules.CanSee(actor, null), null, null, null, domain, UserStatus.Active, 1, 0),
                cancellationToken)).TotalCount
            : null;
        return new NavCountsResponse(projectCount, clientCount, peopleCount);
    }

    public async Task<SearchResponse> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        // With nothing typed, the palette lists everything, first results first, as in the prototype. It shows no totals,
        // so none are counted.
        var text = (query.Q ?? string.Empty).Trim();
        var left = MaxResults;

        var foundProjects = actor.Has(PermissionCodes.ProjectView)
            ? (await projects.SearchAsync(new ProjectSearch(actor.VisibleDomains, text, null, null, null, null, 1, left, CountTotal: false), cancellationToken)).Items
            : [];
        left -= foundProjects.Count;

        var foundClients = left > 0 && actor.Has(PermissionCodes.ClientView)
            ? (await clients.SearchAsync(new ClientSearch(actor.VisibleDomains, text, null, null, 1, left, CountTotal: false), cancellationToken)).Items
            : [];
        left -= foundClients.Count;

        var foundPeople = left > 0 && actor.Has(PermissionCodes.EmployeeView)
            ? (await employees.SearchAsync(
                new EmployeeSearch(actor.VisibleDomains, EmployeeRules.CanSee(actor, null), text, null, null, null, null, 1, left, CountTotal: false),
                cancellationToken)).Items
            : [];

        return new SearchResponse(
            foundProjects.Select(p => new SearchProjectResponse(p.Id, p.DomainCode, p.Title, p.Icon, p.Tint, p.Status)).ToList(),
            foundClients
                .Select(r => new SearchClientResponse(
                    r.Client.Id,
                    r.Client.DomainCode,
                    ClientRules.DisplayName(r.Client.Name, r.Client.Organisation),
                    r.Client.Organisation is null ? r.Client.Label : r.Client.Name))
                .ToList(),
            foundPeople.Select(p => new SearchPersonResponse(p.Id, p.Domain, PersonName.Of(p.FirstName, p.LastName, p.Email), p.Position)).ToList());
    }
}
