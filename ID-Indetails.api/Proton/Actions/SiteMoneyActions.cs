using System.ComponentModel;
using ID_Indetails.api.Business.SiteExpenses;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Data;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Labour.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.SiteExpenses.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record ExpenseArgs(long ProjectId, long? ExpenseId, CreateExpenseRequest? Request);

public sealed record AddExpenseInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("What it was, as the user said.")]
    public required string Label { get; init; }

    public required decimal Amount { get; init; }

    [Description("USD or LBP. Default USD.")]
    public string? Currency { get; init; }

    [Description("assets, services, contractor, software, travel, hardware or other.")]
    public string? Category { get; init; }

    [Description("Default today.")]
    public DateOnly? SpentOn { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Records a Design project expense, through POST /api/projects/{id}/expenses once confirmed. The user types the amount. An
/// Execution site keeps its costs as site expenses instead.
/// </summary>
public sealed class ProposeAddExpenseTool(IProjectService projects, IExpenseService expenses) : ProtonActionTool<AddExpenseInput, ExpenseArgs>
{
    public override string Name => "propose_add_expense";

    public override string Description =>
        "Propose recording a cost spent on a Design project (a contractor, software, travel…) with the amount the user gave. " +
        "The user confirms by typing the amount. An Execution site's costs are site expenses, not this.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ExpensesController>(nameof(ExpensesController.Create));

    protected override async Task<(ExpenseArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        AddExpenseInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        MoneyText.RequireTyped(context, input.Amount);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        if (project.Domain == DomainCodes.Execution)
        {
            throw new ProtonToolInputException("An Execution site's costs are site expenses: use propose_add_site_expenses.");
        }

        var request = new CreateExpenseRequest
        {
            Label = input.Label.Trim(), Amount = input.Amount, Currency = (input.Currency ?? "USD").Trim().ToUpperInvariant(),
            Category = MoneyText.Clean(input.Category)?.ToLowerInvariant(), SpentOn = input.SpentOn, Note = MoneyText.Clean(input.Note),
        };
        // the same amount spent the same day: often the same expense asked for twice; not refused, but said
        var day = request.SpentOn ?? context.Today;
        var same = (await expenses.ListAsync(project.Id, cancellationToken)).Items
            .Count(e => e.Amount == request.Amount && e.Currency == request.Currency && e.SpentOn == day);
        var careful = same == 0 ? "" : $"Careful: {same} expense(s) of {MoneyText.Amount(request.Amount, request.Currency)} that day are already recorded. Confirm only if this is another one. ";
        return (new ExpenseArgs(project.Id, null, request),
            new ProtonPreview($"Record an expense on {project.Title}",
                [
                    new ProtonChange("Expense", null, request.Label),
                    new ProtonChange("Amount", null, MoneyText.Amount(request.Amount, request.Currency)),
                    new ProtonChange("Category", null, request.Category ?? "other"),
                    new ProtonChange("Spent on", null, ProjectText.Date(day)),
                ],
                $"{careful}It lowers the project's profit.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(request.Amount)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(ExpenseArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ExpenseArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var expense = await expenses.CreateAsync(args.ProjectId, args.Request!, cancellationToken);
        return new ProtonActionDone($"Recorded {MoneyText.Amount(expense.Amount, expense.Currency)} for {expense.Label}.", "expense", expense.Id, []);
    }
}

public sealed record DeleteExpenseInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("From list_expenses.")]
    public required long ExpenseId { get; init; }
}

/// <summary>Deletes a project expense, through DELETE /api/projects/{id}/expenses/{expenseId} once confirmed. The user types the amount.</summary>
public sealed class ProposeDeleteExpenseTool(IExpenseService expenses) : ProtonActionTool<DeleteExpenseInput, ExpenseArgs>
{
    public override string Name => "propose_delete_expense";

    public override string Description => "Propose deleting an expense recorded on a project by mistake. It can't be undone; the user types the amount.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ExpensesController>(nameof(ExpensesController.Delete));

    protected override async Task<(ExpenseArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DeleteExpenseInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var expense = (await expenses.ListAsync(input.ProjectId, cancellationToken)).Items.FirstOrDefault(e => e.Id == input.ExpenseId)
            ?? throw new NotFoundException("Expense not found.");
        return (new ExpenseArgs(input.ProjectId, expense.Id, null),
            new ProtonPreview($"Delete the expense {expense.Label}",
                [new ProtonChange("Expense", $"{expense.Label}: {MoneyText.Amount(expense.Amount, expense.Currency)}", "deleted")],
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(expense.Amount), Irreversible: true),
            null);
    }

    protected override object? RequestOf(ExpenseArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(ExpenseArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await expenses.DeleteAsync(args.ProjectId, args.ExpenseId!.Value, cancellationToken);
        return new ProtonActionDone("Deleted the expense.", "expense", args.ExpenseId.Value, []);
    }
}

public sealed record SiteExpenseInput
{
    [Description("What it was, as the user said (\"Cement\", \"Crane hire\").")]
    public required string Name { get; init; }

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    [Description("The total cost, when the user gave a total rather than a unit cost.")]
    public decimal? Amount { get; init; }

    [Description("Who was paid.")]
    public string? Supplier { get; init; }

    public string? Note { get; init; }

    [Description("materials (the default), contractor, equipment, fees or other.")]
    public SiteExpenseCategory? Category { get; init; }

    [Description("The day it was spent. Default today.")]
    public DateOnly? SpentOn { get; init; }
}

/// <summary>A change to a site expense: only the fields the user named; the rest stay as they are.</summary>
public sealed record SiteExpenseChangeInput
{
    public string? Name { get; init; }

    public decimal? Quantity { get; init; }

    [Description("An empty string removes it.")]
    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    [Description("The total cost, when the user gave a total rather than a unit cost.")]
    public decimal? Amount { get; init; }

    [Description("Who was paid. An empty string removes it.")]
    public string? Supplier { get; init; }

    [Description("An empty string removes it.")]
    public string? Note { get; init; }

    [Description("materials, contractor, equipment, fees or other.")]
    public SiteExpenseCategory? Category { get; init; }

    public DateOnly? SpentOn { get; init; }
}

/// <param name="Before">For an update: the expense as the card was made from it. If it changed since, the update is refused.</param>
public sealed record SiteExpenseArgs(
    long ProjectId, long? ExpenseId, AddSiteExpensesRequest? Add, SiteExpenseEntryRequest? Entry, SiteExpenseEntryRequest? Before = null);

public sealed record AddSiteExpensesInput : ProtonActionInput
{
    [Description("An Execution site.")]
    public required long ProjectId { get; init; }

    [Description("Up to 20 expenses, each as the user gave it.")]
    public required IReadOnlyList<SiteExpenseInput> Items { get; init; }
}

/// <summary>
/// Records the costs of an Execution site (materials, a contractor, equipment, fees), through POST
/// /api/projects/{id}/site-expenses once confirmed. They are costs only: nothing is charged to the client. The user types
/// the total cost.
/// </summary>
public sealed class ProposeAddSiteExpensesTool(IProjectService projects, ISiteExpenseService siteExpenses) : ProtonActionTool<AddSiteExpensesInput, SiteExpenseArgs>
{
    private const int Max = 20;

    public override string Name => "propose_add_site_expenses";

    public override string Description =>
        "Propose recording the costs of an Execution site: materials bought, a contractor, equipment or fees, each with its quantity " +
        "and unit cost, or a total. The user confirms by typing the total cost.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<SiteExpensesController>(nameof(SiteExpensesController.Add));

    protected override async Task<(SiteExpenseArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        AddSiteExpensesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Items.Count is 0 or > Max)
        {
            throw new ProtonToolInputException($"Give 1 to {Max} expenses.");
        }

        MoneyText.RequireTyped(context, [.. input.Items.SelectMany(i => new[] { i.UnitCost, i.Amount })]);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        // the cost is the app's own: quantity × unit cost when both are given, otherwise the total
        var items = input.Items.Select(i => new NewSiteExpenseRequest
        {
            Name = i.Name.Trim(), Quantity = i.Quantity, Unit = MoneyText.Clean(i.Unit), UnitCost = i.UnitCost,
            Amount = i.Amount, Supplier = MoneyText.Clean(i.Supplier), Note = MoneyText.Clean(i.Note),
            Category = i.Category, SpentOn = i.SpentOn,
        }).ToList();
        var costs = items.Select(SiteExpenseText.CostOf).ToList();
        if (costs.Any(c => c <= 0))
        {
            throw new ProtonToolInputException("Each expense needs a cost: quantity and unit cost, or a total.");
        }

        var total = costs.Sum();
        var changes = items.Select((i, n) => new ProtonChange(i.Name, null, SiteExpenseText.Line(i, costs[n]))).ToList();
        changes.Add(new ProtonChange("Total cost", null, MoneyText.Amount(total, "USD")));
        return (new SiteExpenseArgs(project.Id, null, new AddSiteExpensesRequest { Items = items }, null),
            new ProtonPreview($"Record expenses for {project.Title}", changes, "It lowers the site's profit.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(total)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(SiteExpenseArgs args) => args.Add;

    protected override async Task<ProtonActionDone> ExecuteAsync(SiteExpenseArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var added = await siteExpenses.AddAsync(args.ProjectId, args.Add!, cancellationToken);
        return new ProtonActionDone($"Recorded {added.Count} expense(s), {MoneyText.Amount(added.Sum(m => m.Cost), "USD")}.", "material", added[0].Id, []);
    }
}

public enum ProtonSiteExpenseStep
{
    Update,
    Delete,
}

public sealed record SiteExpenseStepInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("From the site's expenses list.")]
    public required long ExpenseId { get; init; }

    [Description("update its details, or delete it.")]
    public required ProtonSiteExpenseStep Step { get; init; }

    [Description("For update: only what changes; everything left out stays as it is.")]
    public SiteExpenseChangeInput? Expense { get; init; }
}

/// <summary>
/// Changes or deletes a site expense, through PUT or DELETE once confirmed. Deleting can't be undone, so the user types
/// its cost.
/// </summary>
public sealed class ProposeSiteExpenseStepTool(ISiteExpenseService siteExpenses) : ProtonActionTool<SiteExpenseStepInput, SiteExpenseArgs>
{
    public override string Name => "propose_site_expense_step";

    public override string Description =>
        "Propose correcting a site expense's details or cost, or deleting it. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<SiteExpensesController>(nameof(SiteExpensesController.Update));

    private static readonly ProtonEndpoint Delete = ProtonEndpoint.OfWrite<SiteExpensesController>(nameof(SiteExpensesController.Delete));

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints => [Endpoint, Delete];

    protected override ProtonEndpoint StepOf(SiteExpenseArgs args) => args.Entry is not null ? Endpoint : Delete;

    protected override async Task<(SiteExpenseArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SiteExpenseStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var expense = (await siteExpenses.ListAsync(input.ProjectId, new SiteExpenseListQuery { PageSize = 200 }, cancellationToken)).Items
            .FirstOrDefault(m => m.Id == input.ExpenseId) ?? throw new NotFoundException("Expense not found.");
        if (!expense.CanEdit)
        {
            throw new ProtonToolInputException("It is not the user's to change, or the site is closed.");
        }

        var cost = MoneyText.Amount(expense.Cost, expense.Currency);
        switch (input.Step)
        {
            case ProtonSiteExpenseStep.Update:
                var m = input.Expense ?? throw new ProtonToolInputException("Say what changes on the expense.");
                MoneyText.RequireTyped(context, m.UnitCost, m.Amount);
                // The update replaces the whole expense, as the app's form sends it: start from the expense as it is, so
                // a field the user didn't mention keeps its value instead of being cleared.
                var before = SiteExpenseText.EntryOf(expense);
                var entry = before with
                {
                    Name = MoneyText.Clean(m.Name) ?? before.Name,
                    Quantity = m.Quantity ?? before.Quantity,
                    Unit = m.Unit is null ? before.Unit : MoneyText.Clean(m.Unit),
                    UnitCost = m.UnitCost ?? before.UnitCost,
                    Supplier = m.Supplier is null ? before.Supplier : MoneyText.Clean(m.Supplier),
                    Note = m.Note is null ? before.Note : MoneyText.Clean(m.Note),
                    Category = m.Category ?? before.Category,
                    SpentOn = m.SpentOn ?? before.SpentOn,
                };
                // a total given now counts; otherwise a lump-sum expense keeps its total, and quantity × unit cost works out the rest
                entry = entry with { Amount = m.Amount ?? (entry.Quantity is > 0 && entry.UnitCost is > 0 ? null : before.Amount) };
                var edits = new List<ProtonChange>();
                SettingsText.Compare(edits, "Item", before.Name, entry.Name);
                SettingsText.Compare(edits, "Quantity", SiteExpenseText.Quantity(before), SiteExpenseText.Quantity(entry));
                SettingsText.Compare(edits, "Cost", cost, MoneyText.Amount(SiteExpenseText.CostOf(entry), expense.Currency));
                SettingsText.Compare(edits, "Supplier / payee", before.Supplier ?? "none", entry.Supplier ?? "none");
                SettingsText.Compare(edits, "Category", SnakeCaseEnum.ToDb(expense.Category), SnakeCaseEnum.ToDb(entry.Category ?? expense.Category));
                SettingsText.Compare(edits, "Spent on", ProjectText.Date(expense.SpentOn), ProjectText.Date(entry.SpentOn ?? expense.SpentOn));
                SettingsText.Compare(edits, "Note", before.Note ?? "none", entry.Note ?? "none");
                if (edits.Count == 0)
                {
                    throw new ProtonToolInputException("Nothing would change: say what should be different.");
                }

                return (new SiteExpenseArgs(input.ProjectId, expense.Id, null, entry, before),
                    new ProtonPreview($"Correct the expense {expense.Name}", edits),
                    null);
            default:
                await context.RequireAsync(Delete);
                return (new SiteExpenseArgs(input.ProjectId, expense.Id, null, null),
                    new ProtonPreview($"Delete the expense {expense.Name}", [new ProtonChange(expense.Name, cost, "deleted")],
                        Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(expense.Cost), Irreversible: true),
                    null);
        }
    }

    protected override object? RequestOf(SiteExpenseArgs args) => args.Entry;

    protected override async Task<ProtonActionDone> ExecuteAsync(SiteExpenseArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Entry is { } entry)
        {
            // the card showed the expense as it was: if it changed since, this would put back what someone else changed
            var current = (await siteExpenses.ListAsync(args.ProjectId, new SiteExpenseListQuery { PageSize = 200 }, cancellationToken)).Items
                .FirstOrDefault(m => m.Id == args.ExpenseId);
            if (current is null || args.Before is null || SiteExpenseText.EntryOf(current) != args.Before)
            {
                throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The expense changed since Proton proposed this. Ask again.");
            }

            var updated = await siteExpenses.UpdateAsync(args.ProjectId, args.ExpenseId!.Value, entry, cancellationToken);
            return new ProtonActionDone($"Corrected {updated.Name}.", "material", updated.Id, []);
        }

        await context.RequireAsync(Delete);
        await siteExpenses.DeleteAsync(args.ProjectId, args.ExpenseId!.Value, cancellationToken);
        return new ProtonActionDone("Deleted the expense.", "material", args.ExpenseId.Value, []);
    }
}

internal static class SiteExpenseText
{
    /// <summary>An expense as the update request carries it: quantity and unit cost when it has them, otherwise its total; the rest as it is.</summary>
    public static SiteExpenseEntryRequest EntryOf(SiteExpenseResponse m) => new()
    {
        Name = m.Name, Quantity = m.Quantity, Unit = m.Unit, UnitCost = m.UnitCost,
        Amount = m.Quantity is > 0 && m.UnitCost is > 0 ? null : m.Cost,
        Supplier = m.Supplier, Note = m.Note,
        Currency = m.Currency, SpentOn = m.SpentOn, Category = m.Category, AccountLabelId = m.AccountLabel?.Id,
    };

    /// <summary>The cost the app will store for it, by its own rule (a total over quantity × unit cost sets the unit cost).</summary>
    public static decimal CostOf(SiteExpenseEntryRequest e)
    {
        var stored = SiteExpenseRules.Normalise(new SiteExpenseRules.Entry(e.Quantity, e.UnitCost, e.Amount));
        return SiteExpenseRules.Cost(stored.Quantity, stored.UnitCost, stored.Amount);
    }

    public static string Quantity(SiteExpenseEntryRequest e)
        => e.Quantity is { } q ? $"{q.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} {e.Unit}".Trim() : "none";

    /// <summary>One line of the card: the cost (quantity × unit cost when both were given), then the category and day if named.</summary>
    public static string Line(SiteExpenseEntryRequest e, decimal cost)
    {
        var worked = e.Quantity is { } q && e.UnitCost is { } unit ? $"{q:0.###} {e.Unit} × {MoneyText.Amount(unit, null)} = " : "";
        var category = e.Category is { } c ? $", {SnakeCaseEnum.ToDb(c)}" : "";
        var day = e.SpentOn is { } d ? $", {ProjectText.Date(d)}" : "";
        return $"{worked}{MoneyText.Amount(cost, "USD")}{category}{day}";
    }
}

public sealed record LabourDayInput
{
    public required DateOnly Date { get; init; }

    public required int Workers { get; init; }

    [Description("Pay per worker for that day, in USD.")]
    public required decimal DailyPay { get; init; }
}

public sealed record LabourArgs(long ProjectId, DateOnly? WeekStart, SubmitLabourWeekRequest? Week, SetLabourDefaultsRequest? Defaults);

public sealed record SubmitLabourWeekInput : ProtonActionInput
{
    [Description("An Execution site.")]
    public required long ProjectId { get; init; }

    [Description("The first day of the week (the company's week start).")]
    public required DateOnly WeekStart { get; init; }

    [Description("Each day worked, as the user gave it. The week's days replace any reported before.")]
    public required IReadOnlyList<LabourDayInput> Days { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Reports a site's labour for a week, or replaces it, through PUT /api/projects/{id}/labour-weeks/{weekStart} once
/// confirmed. A crew's day rate is a site cost (decision 13), never a person's pay. The user types the week's total.
/// </summary>
public sealed class ProposeSubmitLabourWeekTool(IProjectService projects, ILabourService labour) : ProtonActionTool<SubmitLabourWeekInput, LabourArgs>
{
    public override string Name => "propose_submit_labour_week";

    public override string Description =>
        "Propose reporting a site's crew for a week: workers and the day rate for each day worked. It replaces what was " +
        "reported for that week. The user confirms by typing the week's total.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<LabourController>(nameof(LabourController.SubmitWeek));

    protected override async Task<(LabourArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SubmitLabourWeekInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        MoneyText.RequireTyped(context, [.. input.Days.Select(d => (decimal?)d.DailyPay)]);
        var current = await labour.GetWeekAsync(project.Id, input.WeekStart, cancellationToken);
        // the week's version as the card read it: the service refuses (409) if the week changed before Confirm
        var week = new SubmitLabourWeekRequest
        {
            Days = input.Days.Select(d => new LabourDayRequest { Date = d.Date, Workers = d.Workers, DailyPay = d.DailyPay }).ToList(),
            Note = MoneyText.Clean(input.Note),
            ExpectedVersion = current.Version,
        };
        var total = week.Days.Sum(d => d.Workers * d.DailyPay);
        var changes = week.Days.Select(d => new ProtonChange(ProjectText.Date(d.Date), null,
            $"{d.Workers} × {MoneyText.Amount(d.DailyPay, "USD")} = {MoneyText.Amount(d.Workers * d.DailyPay, "USD")}")).ToList();
        changes.Add(new ProtonChange("Week total", current.IsReported ? MoneyText.Amount(current.Total, current.Currency) : null, MoneyText.Amount(total, "USD")));
        return (new LabourArgs(project.Id, input.WeekStart, week, null),
            new ProtonPreview($"Report the week of {ProjectText.Date(input.WeekStart)} on {project.Title}", changes,
                current.IsReported ? "It replaces the days reported for that week." : null,
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(total)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(LabourArgs args) => args.Week;

    protected override async Task<ProtonActionDone> ExecuteAsync(LabourArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var week = await labour.SubmitWeekAsync(args.ProjectId, args.WeekStart!.Value, args.Week!, cancellationToken);
        return new ProtonActionDone($"Reported the week: {MoneyText.Amount(week.Total, week.Currency)}.", "labour_week", args.ProjectId, []);
    }
}

public sealed record LabourDefaultsInput : ProtonActionInput
{
    [Description("An Execution site.")]
    public required long ProjectId { get; init; }

    public required int Workers { get; init; }

    [Description("The usual pay per worker per day, in USD.")]
    public required decimal DailyPay { get; init; }
}

/// <summary>Sets the crew size and day rate a site's new weeks start from, through POST …/labour-defaults once confirmed.</summary>
public sealed class ProposeLabourDefaultsTool(IProjectService projects, ILabourService labour) : ProtonActionTool<LabourDefaultsInput, LabourArgs>
{
    public override string Name => "propose_labour_defaults";

    public override string Description =>
        "Propose the usual crew size and day rate a site's new labour weeks start from, from today. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<LabourController>(nameof(LabourController.SetDefaults));

    protected override async Task<(LabourArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        LabourDefaultsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        MoneyText.RequireTyped(context, input.DailyPay);
        var current = (await labour.GetDefaultsAsync(project.Id, cancellationToken)).Current;
        var defaults = new SetLabourDefaultsRequest { Workers = input.Workers, DailyPay = input.DailyPay };
        return (new LabourArgs(project.Id, null, null, defaults),
            new ProtonPreview($"Set the usual crew for {project.Title}",
                [
                    new ProtonChange("Workers", current?.Workers.ToString(), defaults.Workers.ToString()),
                    new ProtonChange("Day rate", current is null ? null : MoneyText.Amount(current.DailyPay, current.Currency), MoneyText.Amount(defaults.DailyPay, "USD")),
                ],
                "From today; weeks already reported stay as they are."),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(LabourArgs args) => args.Defaults;

    protected override async Task<ProtonActionDone> ExecuteAsync(LabourArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await labour.SetDefaultsAsync(args.ProjectId, args.Defaults!, cancellationToken);
        return new ProtonActionDone("Set the usual crew.", "labour_defaults", args.ProjectId, []);
    }
}
