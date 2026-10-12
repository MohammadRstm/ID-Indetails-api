using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.DTO.Response.Materials;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Labour.Abstraction;
using ID_Indetails.api.Services.Materials.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Statements.Abstraction;

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

/// <summary>Records a project expense, through POST /api/projects/{id}/expenses once confirmed. The user types the amount.</summary>
public sealed class ProposeAddExpenseTool(IProjectService projects, IExpenseService expenses) : ProtonActionTool<AddExpenseInput, ExpenseArgs>
{
    public override string Name => "propose_add_expense";

    public override string Description =>
        "Propose recording a cost spent on a project (a contractor, software, travel…) with the amount the user gave. " +
        "The user confirms by typing the amount.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ExpensesController>(nameof(ExpensesController.Create));

    protected override async Task<(ExpenseArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        AddExpenseInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var request = new CreateExpenseRequest
        {
            Label = input.Label.Trim(), Amount = input.Amount, Currency = (input.Currency ?? "USD").Trim().ToUpperInvariant(),
            Category = MoneyText.Clean(input.Category)?.ToLowerInvariant(), SpentOn = input.SpentOn, Note = MoneyText.Clean(input.Note),
        };
        return (new ExpenseArgs(project.Id, null, request),
            new ProtonPreview($"Record an expense on {project.Title}",
                [
                    new ProtonChange("Expense", null, request.Label),
                    new ProtonChange("Amount", null, MoneyText.Amount(request.Amount, request.Currency)),
                    new ProtonChange("Category", null, request.Category ?? "other"),
                    new ProtonChange("Spent on", null, ProjectText.Date(request.SpentOn ?? context.Today)),
                ],
                "It lowers the project's profit.",
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

public sealed record MaterialInput
{
    [Description("The type of material, as the user said (\"Cement\").")]
    public required string Name { get; init; }

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    [Description("The total cost, when the user gave a total rather than a unit cost.")]
    public decimal? Amount { get; init; }

    public string? Supplier { get; init; }

    public string? Note { get; init; }
}

public sealed record MaterialArgs(long ProjectId, long? MaterialId, AddMaterialsRequest? Add, MaterialEntryRequest? Entry, SetMaterialPriceRequest? Price);

public sealed record AddMaterialsInput : ProtonActionInput
{
    [Description("An Execution site.")]
    public required long ProjectId { get; init; }

    [Description("Up to 20 materials bought, each as the user gave it.")]
    public required IReadOnlyList<MaterialInput> Items { get; init; }
}

/// <summary>
/// Records materials bought for an Execution site, through POST /api/projects/{id}/materials once confirmed, at the
/// project's markup (a price of their own is set separately). The user types the total cost.
/// </summary>
public sealed class ProposeAddMaterialsTool(IProjectService projects, IMaterialService materials) : ProtonActionTool<AddMaterialsInput, MaterialArgs>
{
    private const int Max = 20;

    public override string Name => "propose_add_materials";

    public override string Description =>
        "Propose recording materials bought for an Execution site: type, quantity and unit cost, or a total. The user " +
        "confirms by typing the total cost.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<MaterialsController>(nameof(MaterialsController.Add));

    protected override async Task<(MaterialArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        AddMaterialsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Items.Count is 0 or > Max)
        {
            throw new ProtonToolInputException($"Give 1 to {Max} materials.");
        }

        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var items = input.Items.Select(i => new NewMaterialRequest
        {
            Name = i.Name.Trim(), Quantity = i.Quantity, Unit = MoneyText.Clean(i.Unit), UnitCost = i.UnitCost,
            Amount = i.Amount ?? (i.Quantity * i.UnitCost), Supplier = MoneyText.Clean(i.Supplier), Note = MoneyText.Clean(i.Note),
        }).ToList();
        if (items.Any(i => i.Amount is null or <= 0))
        {
            throw new ProtonToolInputException("Each material needs a cost: quantity and unit cost, or a total.");
        }

        var total = items.Sum(i => i.Amount!.Value);
        var changes = items.Select(i => new ProtonChange(i.Name, null,
            $"{(i.Quantity is { } q ? $"{q:0.##} {i.Unit} × {MoneyText.Amount(i.UnitCost ?? 0, null)} = " : "")}{MoneyText.Amount(i.Amount!.Value, "USD")}")).ToList();
        changes.Add(new ProtonChange("Total cost", null, MoneyText.Amount(total, "USD")));
        return (new MaterialArgs(project.Id, null, new AddMaterialsRequest { Items = items }, null, null),
            new ProtonPreview($"Record materials for {project.Title}", changes, "Charged to the client at the project's markup.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(total)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(MaterialArgs args) => args.Add;

    protected override async Task<ProtonActionDone> ExecuteAsync(MaterialArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var added = await materials.AddAsync(args.ProjectId, args.Add!, cancellationToken);
        return new ProtonActionDone($"Recorded {added.Count} material(s), {MoneyText.Amount(added.Sum(m => m.Cost), "USD")}.", "material", added[0].Id, []);
    }
}

public enum ProtonMaterialStep
{
    Update,
    Delete,
    SetPrice,
}

public sealed record MaterialStepInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("From list_materials.")]
    public required long MaterialId { get; init; }

    [Description("update its details, delete it, or set_price for the client.")]
    public required ProtonMaterialStep Step { get; init; }

    [Description("For update: the material as it should be.")]
    public MaterialInput? Material { get; init; }

    [Description("For set_price: the client's price; leave out to go back to the project's markup.")]
    public decimal? ClientPrice { get; init; }
}

/// <summary>
/// Changes, deletes or prices a material not yet on a statement, through PUT, DELETE or PUT …/client-price once
/// confirmed. Deleting can't be undone, so the user types its cost.
/// </summary>
public sealed class ProposeMaterialStepTool(IMaterialService materials) : ProtonActionTool<MaterialStepInput, MaterialArgs>
{
    public override string Name => "propose_material_step";

    public override string Description =>
        "Propose correcting a material's details or cost, deleting it, or setting the client's price for it, while it " +
        "isn't on a statement yet. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<MaterialsController>(nameof(MaterialsController.Update));

    private static readonly ProtonEndpoint Delete = ProtonEndpoint.OfWrite<MaterialsController>(nameof(MaterialsController.Delete));

    private static readonly ProtonEndpoint Price = ProtonEndpoint.OfWrite<MaterialsController>(nameof(MaterialsController.SetPrice));

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints => [Endpoint, Delete, Price];

    protected override ProtonEndpoint StepOf(MaterialArgs args) => args.Price is not null ? Price : args.Entry is not null ? Endpoint : Delete;

    protected override async Task<(MaterialArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        MaterialStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var material = (await materials.ListAsync(input.ProjectId, new MaterialListQuery { PageSize = 200 }, cancellationToken)).Items
            .FirstOrDefault(m => m.Id == input.MaterialId) ?? throw new NotFoundException("Material not found.");
        if (!material.CanEdit)
        {
            throw new ProtonToolInputException("It is on a statement already, or not the user's to change.");
        }

        var cost = MoneyText.Amount(material.Cost, material.Currency);
        switch (input.Step)
        {
            case ProtonMaterialStep.Update:
                var m = input.Material ?? throw new ProtonToolInputException("Give the material as it should be.");
                var entry = new MaterialEntryRequest
                {
                    Name = m.Name.Trim(), Quantity = m.Quantity, Unit = MoneyText.Clean(m.Unit), UnitCost = m.UnitCost,
                    Amount = m.Amount ?? (m.Quantity * m.UnitCost), Supplier = MoneyText.Clean(m.Supplier), Note = MoneyText.Clean(m.Note),
                };
                return (new MaterialArgs(input.ProjectId, material.Id, null, entry, null),
                    new ProtonPreview($"Correct the material {material.Name}",
                        [new ProtonChange(entry.Name, $"{material.Name}: {cost}", MoneyText.Amount(entry.Amount ?? 0, material.Currency))]),
                    null);
            case ProtonMaterialStep.Delete:
                await context.RequireAsync(Delete);
                return (new MaterialArgs(input.ProjectId, material.Id, null, null, null),
                    new ProtonPreview($"Delete the material {material.Name}", [new ProtonChange(material.Name, cost, "deleted")],
                        Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(material.Cost), Irreversible: true),
                    null);
            default:
                await context.RequireAsync(Price);
                return (new MaterialArgs(input.ProjectId, material.Id, null, null, new SetMaterialPriceRequest { ClientPrice = input.ClientPrice }),
                    new ProtonPreview($"Price {material.Name} for the client",
                        [new ProtonChange("Client price",
                            material.ClientPrice is { } was ? MoneyText.Amount(was, material.Currency) : "the project's markup",
                            input.ClientPrice is { } now ? MoneyText.Amount(now, material.Currency) : "the project's markup")]),
                    null);
        }
    }

    protected override object? RequestOf(MaterialArgs args) => (object?)args.Entry ?? args.Price;

    protected override async Task<ProtonActionDone> ExecuteAsync(MaterialArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Entry is { } entry)
        {
            var updated = await materials.UpdateAsync(args.ProjectId, args.MaterialId!.Value, entry, cancellationToken);
            return new ProtonActionDone($"Corrected {updated.Name}.", "material", updated.Id, []);
        }

        if (args.Price is { } price)
        {
            await context.RequireAsync(Price);
            var priced = await materials.SetPriceAsync(args.ProjectId, args.MaterialId!.Value, price, cancellationToken);
            return new ProtonActionDone($"Set the client's price for {priced.Name}.", "material", priced.Id, []);
        }

        await context.RequireAsync(Delete);
        await materials.DeleteAsync(args.ProjectId, args.MaterialId!.Value, cancellationToken);
        return new ProtonActionDone("Deleted the material.", "material", args.MaterialId.Value, []);
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
        var current = await labour.GetWeekAsync(project.Id, input.WeekStart, cancellationToken);
        var week = new SubmitLabourWeekRequest
        {
            Days = input.Days.Select(d => new LabourDayRequest { Date = d.Date, Workers = d.Workers, DailyPay = d.DailyPay }).ToList(),
            Note = MoneyText.Clean(input.Note),
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

public sealed record StatementArgs(long ProjectId, long? StatementId, CreateStatementRequest? Request, RecordDeliveryRequest? Delivery);

public sealed record CreateStatementInput : ProtonActionInput
{
    [Description("An Execution site.")]
    public required long ProjectId { get; init; }

    [Description("detail: one row per material type; total: a single line. Default detail.")]
    public StatementMode? Mode { get; init; }

    [Description("Show materials from earlier statements again.")]
    public bool IncludeEarlier { get; init; }
}

/// <summary>
/// Makes a site's next statement for the client, through POST /api/projects/{id}/statements once confirmed. The card
/// is the service's own preview; the user types the statement's total. Sending it is a separate step in the app.
/// </summary>
public sealed class ProposeCreateStatementTool(IStatementService statements) : ProtonActionTool<CreateStatementInput, StatementArgs>
{
    public override string Name => "propose_create_statement";

    public override string Description =>
        "Propose making a site's next statement of materials for the client, from the server's own preview. The user " +
        "confirms by typing its total; sending it is done in the app.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<StatementsController>(nameof(StatementsController.Create));

    protected override async Task<(StatementArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CreateStatementInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var request = new CreateStatementRequest { Mode = input.Mode ?? StatementMode.Detail, IncludeEarlier = input.IncludeEarlier };
        var preview = await statements.PreviewAsync(input.ProjectId, request, cancellationToken);
        if (preview.Count == 0)
        {
            throw new ProtonToolInputException("There are no new materials to put on a statement.");
        }

        var changes = preview.Types.Take(10).Select(t => new ProtonChange(t.Label, null, MoneyText.Amount(t.Total, preview.Currency))).ToList();
        changes.Add(new ProtonChange("Total", null, MoneyText.Amount(preview.Total, preview.Currency)));
        changes.Add(new ProtonChange("Remaining after it", null, MoneyText.Amount(preview.Remaining, preview.Currency)));
        return (new StatementArgs(input.ProjectId, null, request, null),
            new ProtonPreview($"Make a statement for {preview.ProjectTitle}", changes,
                $"{preview.Count} material(s) for {preview.Client.DisplayName}. It isn't sent until you send it from the app.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(preview.Total)),
            new ProtonRef(ProtonRefType.Project, preview.ProjectId, preview.ProjectTitle, DomainCodes.Execution));
    }

    protected override object? RequestOf(StatementArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(StatementArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var statement = await statements.CreateAsync(args.ProjectId, args.Request!, cancellationToken);
        return new ProtonActionDone($"Made statement {statement.Number}: {MoneyText.Amount(statement.Total, statement.Currency)}.", "statement", statement.Id ?? 0, []);
    }
}

public sealed record StatementDeliveryInput : ProtonActionInput
{
    [Description("From list_statements or the project's statements.")]
    public required long StatementId { get; init; }

    [Description("whatsapp, or download (handed over). Emails are sent from the app.")]
    public required DeliveryChannel Channel { get; init; }

    public string? Recipient { get; init; }

    public string? Note { get; init; }
}

/// <summary>Notes that a statement reached the client outside the app, through POST /api/statements/{id}/deliveries once confirmed.</summary>
public sealed class ProposeStatementDeliveryTool(IStatementService statements) : ProtonActionTool<StatementDeliveryInput, StatementArgs>
{
    public override string Name => "propose_record_statement_delivery";

    public override string Description => "Propose noting that a statement was given to the client outside the app (WhatsApp, by hand). It sends nothing.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<StatementsController>(nameof(StatementsController.RecordDelivery));

    protected override async Task<(StatementArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        StatementDeliveryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Channel == DeliveryChannel.Email)
        {
            throw new ProtonToolInputException("Emails are sent from the app itself, which records them: open the statement for the user instead.");
        }

        var statement = await statements.GetAsync(input.StatementId, cancellationToken);
        var delivery = new RecordDeliveryRequest { Channel = input.Channel, Recipient = MoneyText.Clean(input.Recipient), Note = MoneyText.Clean(input.Note) };
        return (new StatementArgs(statement.ProjectId, statement.Id, null, delivery),
            new ProtonPreview($"Note that statement {statement.Number} was delivered",
                [new ProtonChange("How", null, input.Channel == DeliveryChannel.Whatsapp ? "WhatsApp" : "Handed over"), new ProtonChange("To", null, delivery.Recipient ?? statement.Client.DisplayName)]),
            new ProtonRef(ProtonRefType.Project, statement.ProjectId, statement.ProjectTitle, DomainCodes.Execution));
    }

    protected override object? RequestOf(StatementArgs args) => args.Delivery;

    protected override async Task<ProtonActionDone> ExecuteAsync(StatementArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var statement = await statements.RecordDeliveryAsync(args.StatementId!.Value, args.Delivery!, cancellationToken);
        return new ProtonActionDone($"Noted that statement {statement.Number} was delivered.", "statement", statement.Id ?? 0, []);
    }
}
