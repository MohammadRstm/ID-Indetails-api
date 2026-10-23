using System.ComponentModel;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Finance;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using ID_Indetails.api.Validators.Finance;

namespace ID_Indetails.api.Proton.Tools;

public sealed record DomainFinanceInput
{
    public required ProtonDomain Domain { get; init; }

    [Description("Default this_quarter.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <summary>The billing position as of today, in USD.</summary>
/// <param name="AgreedRevenue">The agreed revenue of the client projects it covers.</param>
public sealed record BillingPosition(
    decimal AgreedRevenue,
    decimal Invoiced,
    int IssuedCount,
    decimal Received,
    decimal OnAccount,
    decimal Outstanding,
    decimal Overdue,
    int OverdueCount,
    int? OldestOverdueDays,
    decimal NotYetInvoiced,
    decimal Drafts,
    int PaymentCount)
{
    public static BillingPosition Of(BillingPositionResponse b) => new(
        b.Value, b.Invoiced, b.IssuedCount, b.Received, b.OnAccount, b.Outstanding, b.Overdue, b.OverdueCount, b.OldestOverdueDays,
        b.NotYetInvoiced, b.Drafts, b.PaymentCount);
}

/// <summary>The period's money, cash basis.</summary>
/// <param name="SoFar">The period runs to today: its figures are so far.</param>
/// <param name="ExpectedRevenue">Agreed in the period (a contract set or raised, an Execution material first sent), not a forecast.</param>
/// <param name="CashProfit">Received less the period's cost.</param>
/// <param name="Unconverted">Costs of the period left out of the USD figures for want of an exchange rate, in their own currency.</param>
public sealed record DomainFinancePeriod(
    DateOnly From,
    DateOnly To,
    bool SoFar,
    decimal Received,
    decimal ExpectedRevenue,
    PeriodCostResponse Cost,
    decimal CashProfit,
    int? CashMarginPct,
    IReadOnlyList<string>? Unconverted,
    decimal LabourHours);

/// <param name="StillToEarn">Client projects' agreed revenue not received yet, each project's never below zero.</param>
/// <param name="UnpricedLabourHours">All time: hours of people with no pay record, left out of the labour cost.</param>
public sealed record DomainFinanceToday(BillingPosition Billing, decimal StillToEarn, decimal UnpricedLabourHours);

/// <summary>All time, against the agreed amounts.</summary>
/// <param name="AgreedProfit">Client projects' agreed revenue less their cost so far.</param>
public sealed record DomainFinanceAllTime(int ClientProjects, int InternalProjects, decimal AgreedRevenue, decimal AgreedProfit, decimal InternalCost);

public sealed record DomainFinance(string Domain, DomainFinancePeriod Period, DomainFinanceToday AsOfToday, DomainFinanceAllTime AllTime, string Note);

/// <summary>
/// A domain's money for a period, as GET /api/finance/{domain}/summary returns it, split into what belongs to the period
/// (cash), what is true today, and the all-time agreed figures, so the model never reads one for another.
/// </summary>
public sealed class GetDomainFinanceTool(IDomainFinanceService finance) : ProtonTool<DomainFinanceInput>
{
    public override string Name => "get_domain_finance";

    public override string Description =>
        "A domain's money for a period, in USD. The period is cash: money received, expected revenue (agreed in the " +
        "period), cost by source and cash profit (received − cost). Also today's billing position (invoiced, received, " +
        "outstanding, overdue, not yet invoiced) and the all-time agreed revenue and profit against it.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Summary));

    protected override async Task<ProtonToolOutput> RunAsync(DomainFinanceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisQuarter, maxDays: 3 * 366);
        var query = new FinancePeriodQuery { From = range.From, To = range.To };
        await context.ValidateAsync(query, cancellationToken);
        var s = await finance.SummaryAsync(input.Domain.Code(), query, cancellationToken);
        var result = new DomainFinance(
            s.Domain,
            new DomainFinancePeriod(
                s.From, s.To, s.To >= context.Today, s.Revenue, s.ExpectedRevenue, s.Cost, s.Profit, ProtonAnalysis.Pct(s.Margin),
                s.Unconverted.Count > 0 ? ProtonAnalysis.Unconverted(s.Unconverted) : null, ProtonText.Hours(s.LabourMinutes)),
            new DomainFinanceToday(BillingPosition.Of(s.Billing), s.StillToEarn, ProtonText.Hours(s.UnpricedLabourMinutes)),
            new DomainFinanceAllTime(s.ClientProjects, s.InternalProjects, s.AllTimeClientRevenue, s.AllTimeClientProfit, s.AllTimeInternalCost),
            "period is cash: what arrived and what was spent in it. as_of_today is today's position whatever the period. " +
            "all_time is against the agreed amounts, not cash.");
        return new ProtonToolOutput(result, [], new DayRange(s.From, s.To));
    }
}

public sealed record MoneyByMonthInput
{
    [Description("design or execution. May be left out only when the user sees one of them.")]
    public ProtonDomain? Domain { get; init; }

    [Description("Months to return, ending with this one, 2 to 36 (default 12).")]
    public int? Months { get; init; }
}

/// <param name="SoFar">The month isn't over: its figures are so far.</param>
/// <param name="CashProfit">Received less the month's cost.</param>
/// <param name="Unconverted">Costs of the month left out of the USD figures for want of an exchange rate, in their own currency.</param>
public sealed record MoneyMonthRow(
    DateOnly Month,
    bool SoFar,
    decimal Received,
    decimal ExpectedRevenue,
    decimal Labour,
    decimal Expenses,
    decimal Materials,
    decimal GroundLabour,
    decimal Cost,
    decimal CashProfit,
    IReadOnlyList<string>? Unconverted);

public sealed record MoneyByMonth(string Domain, IReadOnlyList<MoneyMonthRow> Months, string Note);

/// <summary>
/// A domain's money month by month, as GET /api/finance/{domain}/monthly returns it: what was received, the revenue
/// expected, cost by source and profit, in USD.
/// </summary>
public sealed class GetMoneyByMonthTool(IDomainFinanceService finance) : ProtonTool<MoneyByMonthInput>
{
    public const int DefaultMonths = 12;

    public override string Name => "get_money_by_month";

    public override string Description =>
        "A domain's money month by month, oldest first, in USD: money received, expected revenue (agreed in the month), " +
        "cost by source (labour, expenses, materials, ground labour) and cash profit (received − cost). Use it for monthly " +
        "trends of money received, cost or profit, and for money charts by month.";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Monthly));

    protected override async Task<ProtonToolOutput> RunAsync(MoneyByMonthInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = context.DomainOf(input.Domain);
        var query = new FinanceMonthsQuery { Months = Math.Clamp(input.Months ?? DefaultMonths, 2, FinanceMonthsQueryValidator.MaxMonths) };
        await context.ValidateAsync(query, cancellationToken);
        var months = await finance.MonthlyAsync(domain, query, cancellationToken);
        var thisMonth = new DateOnly(context.Today.Year, context.Today.Month, 1);
        var rows = months.Select(m => new MoneyMonthRow(
            m.Month, m.Month == thisMonth, m.Revenue, m.ExpectedRevenue, m.Labour, m.Expenses, m.Materials, m.GroundLabour, m.Cost, m.Profit,
            m.Unconverted.Count > 0 ? ProtonAnalysis.Unconverted(m.Unconverted) : null)).ToList();
        var period = rows.Count == 0 ? null : new DayRange(rows[0].Month, rows[^1].SoFar ? context.Today : rows[^1].Month.AddMonths(1).AddDays(-1));
        return new ProtonToolOutput(
            new MoneyByMonth(domain, rows,
                "Received: payments on the day they arrived. Expected revenue: what was agreed in the month, not a forecast. Cost: every " +
                "cost dated in the month, in USD at the rate of its day; costs with no rate are listed apart, not counted. Cash profit is " +
                "received less cost, so it swings with when clients pay."),
            [],
            period)
        {
            Datasets = Datasets(rows, period),
        };
    }

    private static IReadOnlyList<Ui.ProtonDataset> Datasets(IReadOnlyList<MoneyMonthRow> rows, DayRange? period)
    {
        var usd = (string key, string label) => Ui.ProtonDatasets.Figure(key, label, Ui.ProtonUnit.Usd);
        var month = Ui.ProtonDatasets.Label("month", "Month", Ui.ProtonColumnKind.Month);
        var words = period is null ? null : Ui.ProtonDatasets.Period(period);
        IReadOnlyList<string>? notes = rows.Any(r => r.Unconverted is not null)
            ? [$"Left out for want of an exchange rate: {string.Join("; ", rows.Where(r => r.Unconverted is not null).Select(r => $"{r.Month:MMM yyyy} {string.Join(", ", r.Unconverted!)}"))}."]
            : null;
        var money = new Ui.ProtonDataset(
            "Money by month",
            [month, usd("received", "Received"), usd("expected_revenue", "Expected revenue"), usd("cost", "Cost"), usd("cash_profit", "Profit (received − cost)")],
            rows.Select(r => new Ui.ProtonDataRow(
                [Ui.ProtonCell.Of(r.Month), Ui.ProtonCell.Of(r.Received), Ui.ProtonCell.Of(r.ExpectedRevenue), Ui.ProtonCell.Of(r.Cost), Ui.ProtonCell.Of(r.CashProfit)],
                Partial: r.SoFar)).ToList(),
            words,
            Notes: notes);
        var cost = new Ui.ProtonDataset(
            "Cost by source, by month",
            [month, usd("labour", "Labour"), usd("expenses", "Expenses"), usd("materials", "Materials"), usd("ground_labour", "Ground labour")],
            rows.Select(r => new Ui.ProtonDataRow(
                [Ui.ProtonCell.Of(r.Month), Ui.ProtonCell.Of(r.Labour), Ui.ProtonCell.Of(r.Expenses), Ui.ProtonCell.Of(r.Materials), Ui.ProtonCell.Of(r.GroundLabour)],
                Partial: r.SoFar)).ToList(),
            words,
            Notes: notes);
        return Ui.ProtonDatasets.WithRows(money, cost);
    }
}

public enum FinanceProjectSort
{
    Revenue,
    Cost,
    Profit,
    Margin,
    Hours,
}

public sealed record FinanceProjectsInput
{
    public required ProtonDomain Domain { get; init; }

    [Description("What to rank by, highest first: revenue (agreed), cost, profit (against the agreed revenue), margin or hours. Default revenue.")]
    public FinanceProjectSort? Sort { get; init; }

    [Description("Lowest first instead.")]
    public bool Ascending { get; init; }

    [Description("Only these projects, e.g. the ones a previous answer listed.")]
    public IReadOnlyList<long>? ProjectIds { get; init; }

    [Description("Rows to return, 1 to 25 (default 10).")]
    public int? Limit { get; init; }
}

/// <summary>Where an open project's cost and profit are heading at its current progress.</summary>
public sealed record ProtonProjection(decimal Cost, decimal AgreedProfit, int? AgreedMarginPct)
{
    public static ProtonProjection? Of(ProjectProjectionResponse? p) => p is null ? null : new(p.Cost, p.Profit, ProtonAnalysis.Pct(p.Margin));
}

/// <param name="AgreedProfit">Agreed revenue less the cost so far.</param>
/// <param name="AgreedMarginPct">Agreed profit as a whole percentage of the agreed revenue; empty until known.</param>
public sealed record FinanceProjectRow(
    long ProjectId,
    string Title,
    string? Client,
    ProjectStatus Status,
    BillingType BillingType,
    decimal AgreedRevenue,
    decimal RecognisedToDate,
    decimal Hours,
    ProjectCostResponse Cost,
    decimal AgreedProfit,
    int? AgreedMarginPct,
    bool MarginKnown,
    ProtonProjection? Projected);

/// <summary>Every project of the domain together: client projects against their agreed revenue, internal ones cost only.</summary>
public sealed record FinanceProjectTotals(
    int ClientProjects, int InternalProjects, decimal AgreedRevenue, decimal ClientCost, decimal AgreedProfit, decimal InternalCost, decimal Cost,
    IReadOnlyList<string>? Unconverted);

public sealed record FinanceProjects(int Total, IReadOnlyList<FinanceProjectRow> Projects, FinanceProjectTotals AllProjects, string Note);

/// <summary>
/// Every project's money, as GET /api/finance/{domain}/projects returns it, ranked here. Cost per hour is left out:
/// on a one-person project it would be that person's rate.
/// </summary>
public sealed class GetFinanceProjectsTool(IDomainFinanceService finance) : ProtonTool<FinanceProjectsInput>
{
    public override string Name => "get_finance_projects";

    public override string Description =>
        "Rank a domain's projects, all time in USD, by agreed revenue (the contract value, not cash), cost, profit " +
        "against the agreed revenue, margin or hours: 'most profitable', 'highest cost', 'compare these projects'.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Projects));

    protected override async Task<ProtonToolOutput> RunAsync(FinanceProjectsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var all = await finance.ProjectsAsync(input.Domain.Code(), cancellationToken);
        var matching = all.Projects
            .Where(p => input.ProjectIds is not { Count: > 0 } || input.ProjectIds.Contains(p.ProjectId))
            .ToList();
        Func<FinanceProjectResponse, decimal> key = (input.Sort ?? FinanceProjectSort.Revenue) switch
        {
            FinanceProjectSort.Cost => p => p.Cost.Total,
            FinanceProjectSort.Profit => p => p.Profit,
            FinanceProjectSort.Margin => p => p.Margin ?? decimal.MinValue,
            FinanceProjectSort.Hours => p => p.Hours,
            _ => p => p.Revenue,
        };
        var ranked = (input.Ascending ? matching.OrderBy(key) : matching.OrderByDescending(key))
            .Take(ProtonText.Limit(input.Limit, 10, 25))
            .Select(p => new FinanceProjectRow(
                p.ProjectId, p.Title, p.ClientName, p.Status, p.BillingType, p.Revenue, p.RecognisedToDate,
                ProtonText.Hours(p.Hours), p.Cost, p.Profit, ProtonAnalysis.Pct(p.Margin), p.MarginKnown, ProtonProjection.Of(p.Projected)))
            .ToList();
        var domain = input.Domain.Code();
        var t = all.Totals;
        var totals = new FinanceProjectTotals(
            t.ClientProjects, t.InternalProjects, t.ClientRevenue, t.ClientCost, t.ClientProfit, t.InternalCost, t.Cost,
            t.Unconverted.Count > 0 ? ProtonAnalysis.Unconverted(t.Unconverted) : null);
        return new ProtonToolOutput(
            new FinanceProjects(matching.Count, ranked, totals,
                "All-time figures in USD, against the agreed revenue, not cash: get_project_financials has what each received. " +
                "Margins that aren't known yet rank last. Internal projects have cost but no revenue."),
            ranked.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, domain)).ToList());
    }
}

/// <param name="Amount">Overdue invoice: its balance. Finished but owed: what is outstanding. Cost warning: the cost.</param>
/// <param name="AgreedRevenue">Cost warning: the project's agreed revenue.</param>
/// <param name="CostOfAgreedPct">Cost warning: the cost as a whole percentage of the agreed revenue.</param>
public sealed record MoneyFollowUp(
    MoneyAttentionKind Kind, long ProjectId, string ProjectTitle, string? ClientName, string? InvoiceNumber, int? DaysOverdue,
    decimal? Amount, decimal? AgreedRevenue, int? CostOfAgreedPct);

public sealed record MoneyAttention(IReadOnlyList<MoneyFollowUp> Items, string Note);

/// <summary>The money follow-ups of a domain, as GET /api/finance/{domain}/attention returns them.</summary>
public sealed class GetMoneyAttentionTool(IDomainFinanceService finance) : ProtonTool<DomainInput>
{
    public override string Name => "get_money_attention";

    public override string Description =>
        "Money that needs attention in a domain: overdue invoices, finished projects still owed money, active client " +
        "projects with no agreed revenue set yet, and (Design) projects whose cost is close to their agreed revenue.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Attention));

    protected override async Task<ProtonToolOutput> RunAsync(DomainInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = input.Domain.Code();
        var items = await finance.AttentionAsync(domain, cancellationToken);
        var followUps = items.Select(i => new MoneyFollowUp(
            i.Kind, i.ProjectId, i.ProjectTitle, i.ClientName, i.InvoiceNumber, i.DaysOverdue, i.Amount, i.Revenue, i.UsedPct)).ToList();
        return new ProtonToolOutput(
            new MoneyAttention(followUps, "At most six items, most urgent first. Amounts in USD."),
            items.Select(i => new ProtonRef(ProtonRefType.Project, i.ProjectId, i.ProjectTitle, domain)).Distinct().ToList());
    }
}

public sealed record InvoicesInput
{
    public long? ProjectId { get; init; }

    public long? ClientId { get; init; }

    [Description("draft, sent, partial, paid or void.")]
    public InvoiceStatus? Status { get; init; }

    public bool? Overdue { get; init; }

    public ProtonDomain? Domain { get; init; }

    [Description("Rows to return, 1 to 25 (default 15), newest first.")]
    public int? Limit { get; init; }
}

public sealed record InvoiceRow(
    long Id,
    string? Number,
    string Domain,
    ProtonNamed Project,
    ProtonNamed Client,
    InvoiceStatus Status,
    bool? Overdue,
    string Currency,
    decimal Total,
    decimal? PaidUsd,
    decimal? Balance,
    DateTime? IssuedAt,
    DateOnly? DueOn,
    DateOnly? ScheduledFor);

public sealed record InvoiceList(int Total, int Shown, IReadOnlyList<InvoiceRow> Invoices);

/// <summary>
/// Invoices, as GET /api/invoices returns them. Paid is in USD; the balance is in the invoice's own currency.
/// </summary>
public sealed class ListInvoicesTool(IInvoiceService invoices) : ProtonTool<InvoicesInput>
{
    public override string Name => "list_invoices";

    public override string Description =>
        "Invoices with their status, totals, paid amount, balance and due dates, filtered by project, client, status or " +
        "overdue. Total and balance are in the invoice's currency; paid_usd is in USD.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<InvoicesController>(nameof(InvoicesController.List));

    protected override async Task<ProtonToolOutput> RunAsync(InvoicesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new InvoiceListQuery
        {
            ProjectId = input.ProjectId,
            ClientId = input.ClientId,
            Status = input.Status,
            Overdue = input.Overdue,
            Domain = input.Domain.Code(),
            PageSize = ProtonText.Limit(input.Limit, 15, 25),
        };
        await context.ValidateAsync(query, cancellationToken);
        var page = await invoices.ListAsync(query, cancellationToken);

        var rows = page.Items.Select(i => new InvoiceRow(
            i.Id, i.Number, i.Domain,
            new ProtonNamed(i.Project.Id, i.Project.Title),
            new ProtonNamed(i.Client.Id, i.Client.DisplayName),
            i.Status, i.IsOverdue, i.Currency, i.Total, i.Paid,
            i.Balance,
            i.IssuedAt, i.DueOn, i.SendOn)).ToList();
        return new ProtonToolOutput(
            new InvoiceList(page.TotalCount, rows.Count, rows),
            rows.Select(r => new ProtonRef(ProtonRefType.Project, r.Project.Id, r.Project.Name, r.Domain))
                .Concat(rows.Select(r => new ProtonRef(ProtonRefType.Client, r.Client.Id, r.Client.Name, r.Domain)))
                .Distinct()
                .ToList());
    }
}

public sealed record PaymentsInput
{
    public long? ProjectId { get; init; }

    public long? ClientId { get; init; }

    public long? InvoiceId { get; init; }

    public bool IncludeVoided { get; init; }

    public ProtonDomain? Domain { get; init; }

    [Description("Rows to return, 1 to 25 (default 15), newest first.")]
    public int? Limit { get; init; }
}

public sealed record PaymentRow(
    long Id,
    string Domain,
    ProtonNamed Project,
    ProtonNamed Client,
    string? InvoiceNumber,
    decimal Amount,
    string Currency,
    decimal AmountUsd,
    DateOnly ReceivedOn,
    PaymentMethod Method,
    string? Reference,
    string? Note,
    bool Voided);

public sealed record PaymentList(int Total, int Shown, IReadOnlyList<PaymentRow> Payments);

public sealed class ListPaymentsTool(IPaymentService payments) : ProtonTool<PaymentsInput>
{
    public override string Name => "list_payments";

    public override string Description => "Client payments received, newest first, by project, client or invoice, with their USD value.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<PaymentsController>(nameof(PaymentsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(PaymentsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new PaymentListQuery
        {
            ProjectId = input.ProjectId,
            ClientId = input.ClientId,
            InvoiceId = input.InvoiceId,
            IncludeVoided = input.IncludeVoided,
            Domain = input.Domain.Code(),
            PageSize = ProtonText.Limit(input.Limit, 15, 25),
        };
        await context.ValidateAsync(query, cancellationToken);
        var page = await payments.ListAsync(query, cancellationToken);

        var rows = page.Items.Select(p => new PaymentRow(
            p.Id, p.Domain,
            new ProtonNamed(p.Project.Id, p.Project.Title),
            new ProtonNamed(p.Client.Id, p.Client.DisplayName),
            p.InvoiceNumber, p.Amount, p.Currency, p.AmountUsd, p.ReceivedOn, p.Method,
            p.Reference, ProtonText.Cut(p.Note, 200), p.VoidedAt is not null)).ToList();
        return new ProtonToolOutput(
            new PaymentList(page.TotalCount, rows.Count, rows),
            rows.Select(r => new ProtonRef(ProtonRefType.Project, r.Project.Id, r.Project.Name, r.Domain)).Distinct().ToList());
    }
}

public sealed record CompanyOverviewInput
{
    [Description("Default this_month. At most 366 days.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <summary>One domain in the overview's period, cash basis, in USD.</summary>
/// <param name="CashProfit">Received less the period's project cost.</param>
/// <param name="Invoiced">Issued in the period.</param>
/// <param name="Outstanding">Unpaid on issued invoices today, whatever the period.</param>
public sealed record DomainOverview(
    string Domain,
    decimal Received,
    decimal Cost,
    decimal CashProfit,
    int? CashMarginPct,
    int ProjectsWithActivity,
    int ActiveProjects,
    int People,
    int Loggers,
    int ClientsServed,
    int AllClients,
    decimal Invoiced,
    decimal Outstanding,
    IReadOnlyList<string>? UnconvertedCosts,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices)
{
    public static DomainOverview Of(DomainOverviewResponse d) => new(
        d.Domain, d.Revenue, d.Cost, d.Profit, ProtonAnalysis.Pct(d.Margin), d.ProjectsWithActivity, d.ActiveProjects, d.People, d.Loggers,
        d.ClientsServed, d.AllClients, d.Invoiced, d.Outstanding,
        d.UnconvertedCosts.Count > 0 ? ProtonAnalysis.Unconverted(d.UnconvertedCosts) : null, d.UnconvertedInvoices);
}

/// <param name="SoFar">The period runs to today: its figures are so far.</param>
/// <param name="CashProfit">Received less project cost, before overhead (the overview's gross profit).</param>
/// <param name="Overhead">Company expenses charged to the period by calendar days, in USD.</param>
public sealed record CompanyOverview(
    DateOnly From,
    DateOnly To,
    bool SoFar,
    decimal Received,
    decimal ProjectCost,
    decimal CashProfit,
    decimal Overhead,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedOverhead,
    decimal Invoiced,
    decimal Outstanding,
    int ProjectsWithActivity,
    int ClientsServed,
    IReadOnlyList<DomainOverview> Domains,
    string Note);

/// <summary>
/// The company overview, as GET /api/company/overview returns it, without admin salaries — and therefore without net
/// profit, which would give the salaries away (gross profit − overhead − net profit).
/// </summary>
public sealed class GetCompanyOverviewTool(ICompanyOverviewService overview) : ProtonTool<CompanyOverviewInput>
{
    public override string Name => "get_company_overview";

    public override string Description =>
        "The whole company's money for a period, in USD, per domain and in total: money received, project cost, cash " +
        "profit (received − project cost, the overview's gross profit), the overhead charged to the period, invoiced, " +
        "and outstanding today. Net profit isn't available here.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<CompanyOverviewController>(nameof(CompanyOverviewController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(CompanyOverviewInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisMonth);
        var o = await overview.GetAsync(range.From, range.To, cancellationToken);
        return new ProtonToolOutput(
            new CompanyOverview(
                o.From, o.To, o.To >= context.Today, o.Revenue, o.Cost, o.GrossProfit, o.Overhead, o.UnconvertedOverhead, o.Invoiced,
                o.Outstanding, o.ProjectsWithActivity, o.ClientsServed, o.Domains.Select(DomainOverview.Of).ToList(),
                "The Company overview page calls received \"revenue\" and \"collected\", and cash profit \"gross profit\". " +
                "Admin salaries and net profit are not available to Proton; that page shows them."),
            [],
            new DayRange(o.From, o.To));
    }
}
