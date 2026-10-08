using ID_Indetails.api.Controllers;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>
/// Reads one project, client or person exactly as its own GET endpoint would for this user: the endpoint's policies
/// first, then the service's domain checks. Proton names a record on screen (opens it, offers it, says the user is
/// looking at it) only after this has found it, so an id the user can't see never turns into a name or a page.
/// </summary>
public sealed class ProtonRecords(IProjectService projects, IClientService clients, IEmployeeService employees, IMessagingService messaging)
{
    public static readonly ProtonEndpoint ProjectGet = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Get));
    public static readonly ProtonEndpoint ClientGet = ProtonEndpoint.Of<ClientsController>(nameof(ClientsController.Get));
    public static readonly ProtonEndpoint PersonGet = ProtonEndpoint.Of<EmployeesController>(nameof(EmployeesController.Get));
    public static readonly ProtonEndpoint Chats = ProtonEndpoint.OfRuleBased<MessagesController>(nameof(MessagesController.Conversations));

    public static ProtonEndpoint EndpointOf(ProtonRefType type) => type switch
    {
        ProtonRefType.Project => ProjectGet,
        ProtonRefType.Client => ClientGet,
        ProtonRefType.Team => Chats,
        _ => PersonGet,
    };

    /// <summary>The record, or null when it doesn't exist or this user may not see it (the two read the same).</summary>
    public async Task<ProtonRef?> FindAsync(ProtonRefType type, long id, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (id <= 0 || !await context.CanCallAsync(EndpointOf(type)))
        {
            return null;
        }

        try
        {
            switch (type)
            {
                case ProtonRefType.Project:
                    var project = await projects.GetAsync(id, cancellationToken);
                    return new ProtonRef(ProtonRefType.Project, project.Id, project.Title, project.Domain);
                case ProtonRefType.Client:
                    var client = await clients.GetAsync(id, cancellationToken);
                    return new ProtonRef(ProtonRefType.Client, client.Id, client.DisplayName, client.Domain);
                case ProtonRefType.Team:
                    // only a team chat the user is in
                    var team = (await messaging.ConversationsAsync(cancellationToken)).Select(c => c.Team).FirstOrDefault(t => t?.Id == id);
                    return team is null ? null : new ProtonRef(ProtonRefType.Team, team.Id, team.Name, team.Domain);
                default:
                    var person = await employees.GetAsync(id, cancellationToken);
                    return new ProtonRef(ProtonRefType.Person, person.Id, $"{person.FirstName} {person.LastName}".Trim(), person.Domain);
            }
        }
        catch (Exception exception) when (exception is NotFoundException or ForbiddenException)
        {
            return null;
        }
    }
}
