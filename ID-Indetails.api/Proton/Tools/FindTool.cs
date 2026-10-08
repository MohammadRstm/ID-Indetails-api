using System.ComponentModel;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record FindInput
{
    [Description("The name or part of it, as the user said it: \"Alpha\", \"the CRM project\", \"John\".")]
    public required string Text { get; init; }

    [Description("What to look for. Leave out to look for projects, clients and people.")]
    public IReadOnlyList<ProtonRefType>? Types { get; init; }
}

public sealed record FoundRecord(ProtonRefType Type, long Id, string Name, string? Domain, string? Detail);

public sealed record FindResult(IReadOnlyList<FoundRecord> Matches, IReadOnlyList<ProtonRefType> NotAllowed);

/// <summary>
/// Turns a name into ids through the same list endpoints the app's search boxes use. Filler words are dropped, and
/// when the whole phrase matches nothing each significant word is tried on its own.
/// </summary>
public sealed class FindTool(IProjectService projects, IClientService clients, IEmployeeService employees)
    : ProtonTool<FindInput>
{
    private const int PerType = 8;

    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "our", "my", "his", "her", "their", "of", "for", "and", "project", "projects",
        "client", "clients", "person", "employee", "called", "named", "site",
    };

    private static readonly ProtonEndpoint ProjectList = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));
    private static readonly ProtonEndpoint ClientList = ProtonEndpoint.Of<ClientsController>(nameof(ClientsController.List));
    private static readonly ProtonEndpoint PeopleList = ProtonEndpoint.Of<EmployeesController>(nameof(EmployeesController.List));

    public override string Name => "find";

    public override string Description =>
        "Find projects, clients or people by name and get their ids. Use it before any other tool whenever the user " +
        "names something. Several matches: ask the user which one. No match: say so.";

    public override ProtonEndpoint Endpoint => ProjectList;

    protected override async Task<ProtonToolOutput> RunAsync(FindInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var words = input.Text
            .Split([' ', ',', '.', '\'', '"', '?', '!'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Filler.Contains(w) && !w.Equals("'s", StringComparison.Ordinal))
            .Select(w => w.EndsWith("'s", StringComparison.Ordinal) ? w[..^2] : w)
            .ToList();
        if (words.Count == 0)
        {
            throw new ProtonToolInputException("Give a name to look for.");
        }

        // teams are found among the user's own chats, by find_recipient
        var types = (input.Types is { Count: > 0 } ? input.Types.Distinct() : Enum.GetValues<ProtonRefType>()).Where(t => t != ProtonRefType.Team);
        var matches = new List<FoundRecord>();
        var notAllowed = new List<ProtonRefType>();
        foreach (var type in types)
        {
            if (!await context.CanCallAsync(EndpointOf(type)))
            {
                notAllowed.Add(type);
                continue;
            }

            var found = await SearchAsync(type, string.Join(' ', words), context, cancellationToken);
            foreach (var word in words.Where(w => w.Length >= 3).OrderByDescending(w => w.Length))
            {
                if (found.Count > 0)
                {
                    break;
                }

                found = await SearchAsync(type, word, context, cancellationToken);
            }

            matches.AddRange(found);
        }

        var phrase = string.Join(' ', words);
        var ranked = matches
            .DistinctBy(m => (m.Type, m.Id))
            .OrderBy(m => Rank(m.Name, phrase))
            .Take(PerType)
            .ToList();
        return new ProtonToolOutput(
            new FindResult(ranked, notAllowed),
            ranked.Select(m => new ProtonRef(m.Type, m.Id, m.Name, m.Domain)).ToList());
    }

    private static ProtonEndpoint EndpointOf(ProtonRefType type) => type switch
    {
        ProtonRefType.Project => ProjectList,
        ProtonRefType.Client => ClientList,
        _ => PeopleList,
    };

    private static int Rank(string name, string phrase)
        => name.Equals(phrase, StringComparison.OrdinalIgnoreCase) ? 0
            : name.StartsWith(phrase, StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains(phrase, StringComparison.OrdinalIgnoreCase) ? 2
            : 3;

    private async Task<List<FoundRecord>> SearchAsync(
        ProtonRefType type, string search, ProtonToolContext context, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case ProtonRefType.Project:
            {
                var query = new ProjectListQuery { Search = search, PageSize = PerType };
                await context.ValidateAsync(query, cancellationToken);
                var page = await projects.ListAsync(query, cancellationToken);
                return page.Items
                    .Select(p => new FoundRecord(type, p.Id, p.Title, p.Domain, $"{p.Status}, client: {p.Client?.DisplayName ?? "internal"}"))
                    .ToList();
            }
            case ProtonRefType.Client:
            {
                var query = new ClientListQuery { Search = search, IncludeArchived = true, PageSize = PerType };
                await context.ValidateAsync(query, cancellationToken);
                var page = await clients.ListAsync(query, cancellationToken);
                return page.Items
                    .Select(c => new FoundRecord(type, c.Id, c.DisplayName, c.Domain, c.IsArchived ? "archived" : $"{c.ProjectCount} projects"))
                    .ToList();
            }
            default:
            {
                var query = new EmployeeListQuery { Search = search, PageSize = PerType };
                await context.ValidateAsync(query, cancellationToken);
                var page = await employees.ListAsync(query, cancellationToken);
                return page.Items
                    .Select(e => new FoundRecord(type, e.Id, $"{e.FirstName} {e.LastName}".Trim(), e.Domain, $"{e.Role}, {e.Position ?? "no position"}"))
                    .ToList();
            }
        }
    }
}
