using System.ComponentModel;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Clients.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record ListClientsInput
{
    [Description("Name, organisation, label, note or contact words.")]
    public string? Search { get; init; }

    [Description("active (has an active or on-hold project), owing (money outstanding) or empty (no projects).")]
    public ClientFilter? Filter { get; init; }

    [Description("name (default), projects, hours, activity, revenue, profit, margin or outstanding.")]
    public ClientSort? Sort { get; init; }

    [Description("Reverse the sort order.")]
    public bool Reverse { get; init; }

    public bool IncludeArchived { get; init; }

    public ProtonDomain? Domain { get; init; }

    [Description("Rows to return, 1 to 25 (default 15).")]
    public int? Limit { get; init; }
}

public sealed record ClientMoneyRow(decimal Revenue, decimal Cost, decimal Profit, decimal? Margin, decimal Received, decimal Outstanding, decimal Overdue);

public sealed record ClientRow(
    long Id,
    string Name,
    string? Organisation,
    string? Label,
    string Domain,
    bool Archived,
    int Projects,
    string? PrimaryContact,
    decimal? Hours,
    DateTime? LastWorkAt,
    ClientMoneyRow? Money);

public sealed record ClientList(int Total, int Shown, IReadOnlyList<ClientRow> Clients);

public sealed class ListClientsTool(IClientService clients) : ProtonTool<ListClientsInput>
{
    public override string Name => "list_clients";

    public override string Description =>
        "List and count clients, with filters and sorting. Rows carry hours and money only when this user may see them.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ClientsController>(nameof(ClientsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(ListClientsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new ClientListQuery
        {
            Search = input.Search,
            Filter = input.Filter,
            Sort = input.Sort ?? ClientSort.Name,
            Reverse = input.Reverse,
            IncludeArchived = input.IncludeArchived,
            Domain = input.Domain.Code(),
            PageSize = ProtonText.Limit(input.Limit, 15, 25),
        };
        await context.ValidateAsync(query, cancellationToken);
        var page = await clients.ListAsync(query, cancellationToken);

        var rows = page.Items.Select(c => new ClientRow(
            c.Id, c.DisplayName, c.Organisation, c.Label, c.Domain, c.IsArchived, c.ProjectCount,
            c.PrimaryContact?.Value,
            c.Work is { } w ? ProtonText.Hours(w.TotalMinutes) : null,
            c.Work?.LastStartedAt,
            c.Money is { } m ? new ClientMoneyRow(m.Revenue, m.Cost.Total, m.Profit, m.Margin, m.Billing.Received, m.Billing.Outstanding, m.Billing.Overdue) : null))
            .ToList();
        return new ProtonToolOutput(
            new ClientList(page.TotalCount, rows.Count, rows),
            rows.Select(c => new ProtonRef(ProtonRefType.Client, c.Id, c.Name, c.Domain)).ToList());
    }
}

public sealed record ClientIdInput
{
    public required long ClientId { get; init; }
}

public sealed record ClientContactRow(ContactKind Kind, string Value, string? Label, bool Primary, bool Whatsapp);

public sealed record ClientPersonHours(long Id, string Name, decimal Hours);

public sealed record ClientWork(decimal Hours, int Entries, int People, IReadOnlyList<ClientPersonHours> TopPeople);

public sealed record ClientProjectRow(long ProjectId, string Title, ProjectStatus Status, decimal Hours, decimal Revenue, decimal Cost, decimal Profit, decimal? Margin, BillingTotalsResponse Billing);

public sealed record ClientCard(
    long Id,
    string Name,
    string? Organisation,
    string? Label,
    string? Note,
    string Domain,
    bool Archived,
    DateTime CreatedAt,
    int Projects,
    IReadOnlyList<ClientContactRow> Contacts,
    ClientWork? Work,
    ClientMoneyResponse? Money,
    IReadOnlyList<ClientProjectRow>? ProjectMoney,
    string AboutTheFigures);

/// <summary>One client, as GET /api/clients/{id} returns it.</summary>
public sealed class GetClientTool(IClientService clients) : ProtonTool<ClientIdInput>
{
    public override string Name => "get_client";

    public override string Description =>
        "A client's details, contacts, logged hours, and (for people with financial access) revenue, received, " +
        "outstanding and each project's money. For their projects' status use list_projects with client_id.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ClientsController>(nameof(ClientsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ClientIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var c = await clients.GetAsync(input.ClientId, cancellationToken);
        var card = new ClientCard(
            c.Id, c.DisplayName, c.Organisation, c.Label, ProtonText.Cut(c.Note, 500), c.Domain, c.IsArchived, c.CreatedAt, c.ProjectCount,
            c.Contacts.Select(x => new ClientContactRow(x.Kind, x.Value, x.Label, x.IsPrimary, x.IsWhatsapp)).ToList(),
            c.Work is { } w
                ? new ClientWork(ProtonText.Hours(w.TotalMinutes), w.EntryCount, w.ContributorCount,
                    w.People.Select(p => new ClientPersonHours(p.Id, p.Name, ProtonText.Hours(p.Minutes))).ToList())
                : null,
            c.Money,
            c.ProjectMoney?.Select(p => new ClientProjectRow(p.ProjectId, p.Title, p.Status, ProtonText.Hours(p.Hours), p.Revenue, p.Cost, p.Profit, p.Margin, p.Billing)).ToList(),
            "Money is all time, in USD.");
        return new ProtonToolOutput(
            card,
            [new ProtonRef(ProtonRefType.Client, c.Id, c.DisplayName, c.Domain),
             .. (card.ProjectMoney ?? []).Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, c.Domain))])
        {
            Datasets = Datasets(c.DisplayName, c.Domain, c.Work, card.ProjectMoney, context.Today),
        };
    }

    /// <summary>The client as tables: hours by month over the last year, and each project's money when the user may see it.</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(
        string name, string domain, ClientWorkResponse? work, IReadOnlyList<ClientProjectRow>? projects, DateOnly today)
    {
        var monthly = work is null ? null : ProtonDatasets.Months(
            $"{name}: hours by month", work.Monthly.Select(m => (m.Month, ProtonText.Hours(m.Minutes))).ToList(), today);
        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        var money = projects is null ? null : new ProtonDataset(
            $"{name}: money by project",
            [ProtonDatasets.Label("project", "Project"), ProtonDatasets.Label("status", "Status", ProtonColumnKind.Status), usd("revenue", "Revenue"),
                usd("cost", "Cost"), usd("profit", "Profit"), ProtonDatasets.Figure("margin", "Margin", ProtonUnit.Percent)],
            projects.Take(ProtonDatasets.MaxRows).Select(p => ProtonDatasets.Row(
                new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, domain),
                ProtonCell.Of(p.Title), ProtonDatasets.Status(p.Status), ProtonCell.Of(p.Revenue), ProtonCell.Of(p.Cost), ProtonCell.Of(p.Profit),
                ProtonCell.Of(ProtonAnalysis.Pct(p.Margin)))).ToList(),
            "All time",
            projects.Count);
        return ProtonDatasets.WithRows(monthly, money);
    }
}
