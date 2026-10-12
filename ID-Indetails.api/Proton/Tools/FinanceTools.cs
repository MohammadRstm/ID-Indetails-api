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

/// <summary>A domain's money for a period, as GET /api/finance/{domain}/summary returns it.</summary>
public sealed class GetDomainFinanceTool(IDomainFinanceService finance) : ProtonTool<DomainFinanceInput>
{
    public override string Name => "get_domain_finance";

    public override string Description =>
        "A domain's money for a period, in USD: revenue received, expected revenue, cost by source, profit, margin, " +
        "and the billing position (invoiced, received, outstanding, overdue, not yet invoiced).";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Summary));

    protected override async Task<ProtonToolOutput> RunAsync(DomainFinanceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisQuarter, maxDays: 3 * 366);
        var query = new FinancePeriodQuery { From = range.From, To = range.To };
        await context.ValidateAsync(query, cancellationToken);
        var summary = await finance.SummaryAsync(input.Domain.Code(), query, cancellationToken);
        return new ProtonToolOutput(summary, [], new DayRange(summary.From, summary.To));
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
    decimal Profit,
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
        "A domain's money month by month, oldest first, in USD: money received, expected revenue, cost by source " +
        "(labour, expenses, materials, ground labour) and profit (received less cost). Use it for monthly trends of " +
        "revenue, cost or profit, and for money charts by month.";

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
                "Received: payments on the day they arrived. Cost: every cost dated in the month, in USD at the rate of its day; costs with no " +
                "rate are listed apart, not counted. Profit is received less cost, so it swings with when clients pay."),
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
            [month, usd("received", "Received"), usd("expected_revenue", "Expected revenue"), usd("cost", "Cost"), usd("profit", "Profit")],
            rows.Select(r => new Ui.ProtonDataRow(
                [Ui.ProtonCell.Of(r.Month), Ui.ProtonCell.Of(r.Received), Ui.ProtonCell.Of(r.ExpectedRevenue), Ui.ProtonCell.Of(r.Cost), Ui.ProtonCell.Of(r.Profit)],
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

    [Description("What to rank by, highest first. Default revenue.")]
    public FinanceProjectSort? Sort { get; init; }

    [Description("Lowest first instead.")]
    public bool Ascending { get; init; }

    [Description("Only these projects, e.g. the ones a previous answer listed.")]
    public IReadOnlyList<long>? ProjectIds { get; init; }

    [Description("Rows to return, 1 to 25 (default 10).")]
    public int? Limit { get; init; }
}

public sealed record FinanceProjectRow(
    long ProjectId,
    string Title,
    string? Client,
    ProjectStatus Status,
    BillingType BillingType,
    decimal Revenue,
    decimal RecognisedToDate,
    decimal Hours,
    ProjectCostResponse Cost,
    decimal Profit,
    decimal? Margin,
    bool MarginKnown,
    ProjectProjectionResponse? Projected);

public sealed record FinanceProjects(int Total, IReadOnlyList<FinanceProjectRow> Projects, FinanceProjectTotalsResponse AllProjects, string Note);

/// <summary>
/// Every project's money, as GET /api/finance/{domain}/projects returns it, ranked here. Cost per hour is left out:
/// on a one-person project it would be that person's rate.
/// </summary>
public sealed class GetFinanceProjectsTool(IDomainFinanceService finance) : ProtonTool<FinanceProjectsInput>
{
    public override string Name => "get_finance_projects";

    public override string Description =>
        "Rank a domain's projects by revenue, cost, profit, margin or hours (all time, USD): 'most profitable', " +
        "'highest cost', 'compare these projects'.";

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
                ProtonText.Hours(p.Hours), p.Cost, p.Profit, p.Margin, p.MarginKnown, p.Projected))
            .ToList();
        var domain = input.Domain.Code();
        return new ProtonToolOutput(
            new FinanceProjects(matching.Count, ranked, all.Totals,
                "All-time figures in USD. Margins that aren't known yet rank last. Internal projects have cost but no revenue."),
            ranked.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, domain)).ToList());
    }
}

public sealed record MoneyAttention(IReadOnlyList<MoneyAttentionResponse> Items, string Note);

/// <summary>The money follow-ups of a domain, as GET /api/finance/{domain}/attention returns them.</summary>
public sealed class GetMoneyAttentionTool(IDomainFinanceService finance) : ProtonTool<DomainInput>
{
    public override string Name => "get_money_attention";

    public override string Description =>
        "Money that needs attention in a domain: overdue invoices, finished projects still owed money, projects with " +
        "no payment yet, and (Design) projects whose cost is close to their revenue.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Attention));

    protected override async Task<ProtonToolOutput> RunAsync(DomainInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = input.Domain.Code();
        var items = await finance.AttentionAsync(domain, cancellationToken);
        return new ProtonToolOutput(
            new MoneyAttention(items, "At most six items, most urgent first."),
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
    decimal? Paid,
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

    public override string Description => "Invoices with their status, totals, paid amount, balance and due dates, filtered by project, client, status or overdue.";

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

public sealed record CompanyOverview(
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    decimal ProjectCost,
    decimal GrossProfit,
    decimal Overhead,
    IReadOnlyList<CurrencyTotalResponse> UnconvertedOverhead,
    decimal Invoiced,
    decimal Collected,
    decimal Outstanding,
    int ProjectsWithActivity,
    int ClientsServed,
    IReadOnlyList<DomainOverviewResponse> Domains,
    string Note);

/// <summary>
/// The company overview, as GET /api/company/overview returns it, without admin salaries — and therefore without net
/// profit, which would give the salaries away (gross profit − overhead − net profit).
/// </summary>
public sealed class GetCompanyOverviewTool(ICompanyOverviewService overview) : ProtonTool<CompanyOverviewInput>
{
    public override string Name => "get_company_overview";

    public override string Description =>
        "The whole company's money for a period, in USD: revenue, project cost, gross profit, overhead, invoiced, " +
        "collected and outstanding, per domain and in total. Net profit isn't available here.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<CompanyOverviewController>(nameof(CompanyOverviewController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(CompanyOverviewInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisMonth);
        var o = await overview.GetAsync(range.From, range.To, cancellationToken);
        return new ProtonToolOutput(
            new CompanyOverview(
                o.From, o.To, o.Revenue, o.Cost, o.GrossProfit, o.Overhead, o.UnconvertedOverhead, o.Invoiced, o.Collected,
                o.Outstanding, o.ProjectsWithActivity, o.ClientsServed, o.Domains,
                "Admin salaries and net profit are not available to Proton; the Company overview page shows them."),
            [],
            new DayRange(o.From, o.To));
    }
}
