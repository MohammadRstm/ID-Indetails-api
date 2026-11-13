using System.ComponentModel;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.SiteExpenses.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record ExpensesInput
{
    public required long ProjectId { get; init; }

    [Description("Rows to return, 1 to 50 (default 25), newest first.")]
    public int? Limit { get; init; }
}

public sealed record ExpenseRow(long Id, string Label, decimal Amount, string Currency, string Category, DateOnly SpentOn, string? Note);

/// <param name="Totals">One total per currency, of every expense; amounts in different currencies are never added together.</param>
public sealed record ExpenseList(int Total, int Shown, IReadOnlyList<CurrencyTotalResponse> Totals, IReadOnlyList<ExpenseRow> Expenses);

/// <summary>A Design project's expenses, as GET /api/projects/{id}/expenses returns them, with their ids for the expense cards.</summary>
public sealed class ListExpensesTool(IExpenseService expenses) : ProtonTool<ExpensesInput>
{
    public override string Name => "list_expenses";

    public override string Description =>
        "A Design project's expenses (printing, transport, fees…), newest first: what, how much and in which currency, " +
        "the category and the day spent, with a total per currency. Gives the ids propose_delete_expense needs.";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ExpensesController>(nameof(ExpensesController.List));

    protected override async Task<ProtonToolOutput> RunAsync(ExpensesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var list = await expenses.ListAsync(input.ProjectId, cancellationToken);
        var rows = list.Items.Take(ProtonText.Limit(input.Limit, 25, 50))
            .Select(e => new ExpenseRow(e.Id, e.Label, e.Amount, e.Currency, e.Category, e.SpentOn, ProtonText.Cut(e.Note, 200)))
            .ToList();
        return ProtonToolOutput.Of(new ExpenseList(list.Items.Count, rows.Count, list.Totals, rows));
    }
}

public sealed record SiteExpensesInput
{
    public required long ProjectId { get; init; }

    [Description("Only expenses spent in this period. Leave out for any date.")]
    public ProtonPeriod? SpentIn { get; init; }

    [Description("First day, with spent_in custom.")]
    public DateOnly? From { get; init; }

    [Description("Last day, with spent_in custom.")]
    public DateOnly? To { get; init; }

    public SiteExpenseCategory? Category { get; init; }

    [Description("Words in the item, who was paid, the note or the unit.")]
    public string? Search { get; init; }

    [Description("Rows to return, 1 to 50 (default 25), latest added first.")]
    public int? Limit { get; init; }
}

/// <param name="Cost">Quantity × unit cost, or the total typed in, in <paramref name="Currency"/>.</param>
/// <param name="Supplier">Who was paid.</param>
/// <param name="CanEdit">Whether the user may correct or delete it now.</param>
public sealed record SiteExpenseRow(
    long Id,
    string Item,
    decimal? Quantity,
    string? Unit,
    decimal? UnitCost,
    decimal Cost,
    string Currency,
    string? Supplier,
    SiteExpenseCategory Category,
    DateOnly SpentOn,
    string? AccountLabel,
    string AddedBy,
    string? Note,
    bool CanEdit);

/// <param name="Matching">Totals of everything the filters match: the cost in USD, and one total per currency.</param>
/// <param name="All">Totals of every expense of the site.</param>
public sealed record SiteExpenseList(
    int Total,
    int Shown,
    SiteExpenseTotalsResponse Matching,
    SiteExpenseTotalsResponse All,
    IReadOnlyList<SiteExpenseRow> Expenses);

/// <summary>
/// An Execution site's expenses, as GET /api/projects/{id}/site-expenses returns them, with their ids for the site
/// expense cards. Their costs are shown to whoever sees the site, as on its page.
/// </summary>
public sealed class ListSiteExpensesTool(ISiteExpenseService siteExpenses) : ProtonTool<SiteExpensesInput>
{
    public override string Name => "list_site_expenses";

    public override string Description =>
        "An Execution site's expenses (materials, contractors, equipment, fees…), latest added first: the item, quantity " +
        "and unit cost, the cost in its currency, who was paid, the category, the day spent, the account label and who " +
        "added it, with totals of what matches. Gives the ids propose_site_expense_step needs.";

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<SiteExpensesController>(nameof(SiteExpensesController.List));

    protected override async Task<ProtonToolOutput> RunAsync(SiteExpensesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var spent = input.SpentIn is null ? null : context.Period(input.SpentIn, input.From, input.To, ProtonPeriod.ThisMonth, maxDays: 3 * 366);
        var query = new SiteExpenseListQuery
        {
            From = spent?.From,
            To = spent?.To,
            Category = input.Category,
            Search = input.Search,
            PageSize = ProtonText.Limit(input.Limit, 25, 50),
        };
        await context.ValidateAsync(query, cancellationToken);
        var list = await siteExpenses.ListAsync(input.ProjectId, query, cancellationToken);
        var rows = list.Items.Select(e => new SiteExpenseRow(
            e.Id, e.Name, e.Quantity, e.Unit, e.UnitCost, e.Cost, e.Currency, e.Supplier, e.Category, e.SpentOn,
            e.AccountLabel?.Name, e.AddedBy.Name, ProtonText.Cut(e.Note, 200), e.CanEdit)).ToList();
        return new ProtonToolOutput(new SiteExpenseList(list.TotalCount, rows.Count, list.Matching, list.All, rows), [], spent);
    }
}

public sealed record OverheadListInput
{
    [Description("Also the expenses that have ended. Default false: a cancelled one still runs to the end of its month, and can be resumed until then.")]
    public bool IncludeEnded { get; init; }

    [Description("The month whose one-off expenses to list, as YYYY-MM, never a future one. Default: this month.")]
    public string? OneOffMonth { get; init; }
}

public sealed record OverheadAmountRow(DateOnly From, decimal Amount);

/// <param name="CanEdit">The user may change or remove it: its month is still the current one.</param>
public sealed record OneOffRow(long Id, string Name, ProtonNamed Category, string Currency, decimal Amount, DateOnly SpentOn, bool CanEdit);

/// <param name="Month">The first day of the month listed.</param>
/// <param name="IsOpen">It is the current month, so its one-offs can still be changed.</param>
/// <param name="Totals">One total per currency.</param>
public sealed record OneOffList(DateOnly Month, bool IsOpen, IReadOnlyList<CurrencyTotalResponse> Totals, IReadOnlyList<OneOffRow> Items);

/// <param name="ThisMonth">What it costs this month, in <paramref name="Currency"/>; empty when it isn't charged this month.</param>
/// <param name="Amounts">Its monthly amount from each month it changed, a coming change included.</param>
public sealed record OverheadRow(
    long Id,
    string Name,
    ProtonNamed Category,
    string Currency,
    decimal? ThisMonth,
    DateOnly StartedOn,
    DateOnly? EndedOn,
    IReadOnlyList<OverheadAmountRow> Amounts);

/// <param name="Lines">How many expenses use it, past ones included; only one used by none can be removed.</param>
public sealed record OverheadCategoryRow(long Id, string Name, int Lines);

/// <param name="ThisMonth">This month's recurring company expenses, one total per currency.</param>
/// <param name="OneOffs">The one-off company expenses of the month asked for (this month by default).</param>
public sealed record OverheadList(
    IReadOnlyList<CurrencyTotalResponse> ThisMonth,
    IReadOnlyList<OverheadCategoryRow> Categories,
    IReadOnlyList<OverheadRow> Expenses,
    OneOffList OneOffs);

/// <summary>
/// The company's recurring expenses (overhead), their categories and a month's one-off expenses, as GET
/// /api/company/overhead, /groups and /one-offs return them, with the ids the overhead and company expense cards and
/// forms need.
/// </summary>
public sealed class ListOverheadTool(IOverheadService overhead) : ProtonTool<OverheadListInput>
{
    public override string Name => "list_overhead";

    public override string Description =>
        "The company's recurring expenses (rent, software… the app's Company expenses), each with its category, currency, " +
        "this month's amount, when it started and ended and its amount changes, the categories, and a month's one-off " +
        "expenses (a refrigerator, a repair: paid once, counted in full on the day), with a total per currency. Gives " +
        "the ids propose_overhead and the company_expense form need.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<OverheadController>(nameof(OverheadController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(OverheadListInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await overhead.GetAsync(cancellationToken);
        var groups = await overhead.GroupsAsync(cancellationToken);
        var lines = current.Lines
            .Where(l => input.IncludeEnded || !OverheadRules.HasEnded(l.EndedOn, context.Today))
            .Select(l => new OverheadRow(
                l.Id, l.Name, new ProtonNamed(l.Group.Id, l.Group.Name), l.Currency, l.CurrentAmount, l.StartedOn, l.EndedOn,
                l.Amounts.Select(a => new OverheadAmountRow(a.EffectiveFrom, a.Amount)).ToList()))
            .ToList();
        var oneOffs = await overhead.OneOffsAsync(input.OneOffMonth?.Trim(), cancellationToken);
        var oneOffRows = oneOffs.Items
            .Select(e => new OneOffRow(e.Id, e.Name, new ProtonNamed(e.Group.Id, e.Group.Name), e.Currency, e.Amount, e.SpentOn, e.CanEdit))
            .ToList();
        return ProtonToolOutput.Of(new OverheadList(
            current.MonthTotals,
            groups.Select(g => new OverheadCategoryRow(g.Id, g.Name, g.Lines)).ToList(),
            lines,
            new OneOffList(oneOffs.Month, oneOffs.IsOpen, oneOffs.Totals, oneOffRows)));
    }
}
