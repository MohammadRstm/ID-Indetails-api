using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>
/// The billing settings Proton may change. Bank details are not among them: they never reach Proton, are never stored
/// with a proposal, and are read again from the database when the change is made. Overdue reminders aren't either:
/// turning them on emails clients automatically, which stays in the app (like every automatic email).
/// </summary>
public sealed record BillingSettingsChange(string InvoicePrefix, int DefaultTermsDays, string? TermsText, string? Footer);

public sealed record BillingSettingsArgs(string DomainCode, BillingSettingsChange Change, BillingSettingsChange Before);

public sealed record BillingSettingsInput : ProtonActionInput
{
    [Description("design or execution.")]
    public required string Domain { get; init; }

    [Description("The start of every new invoice number (\"HL\" gives HL-2026-001).")]
    public string? InvoicePrefix { get; init; }

    public int? DefaultTermsDays { get; init; }

    [Description("Printed terms; an empty string removes them.")]
    public string? TermsText { get; init; }

    [Description("Printed footer; an empty string removes it.")]
    public string? Footer { get; init; }
}

/// <summary>
/// Changes a domain's invoice numbering, terms and footer, through PUT /api/billing-settings/{domain} once confirmed.
/// Its bank details and overdue reminders stay as they are, read again from the database.
/// </summary>
public sealed class ProposeBillingSettingsTool(IBillingSettingsService settings) : ProtonActionTool<BillingSettingsInput, BillingSettingsArgs>
{
    public override string Name => "propose_billing_settings";

    public override string Description =>
        "Propose changing a domain's invoice prefix, default payment terms, or printed terms and footer. Overdue " +
        "reminders (they email clients) and bank details are changed in the app only. Nothing changes until the user " +
        "confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<BillingSettingsController>(nameof(BillingSettingsController.Save));

    protected override async Task<(BillingSettingsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        BillingSettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = ClientText.DomainOf(input.Domain, context);
        var before = CompanyMoneyText.ChangeOf(await settings.GetAsync(domain, cancellationToken));
        var after = before with
        {
            InvoicePrefix = MoneyText.Clean(input.InvoicePrefix) ?? before.InvoicePrefix,
            DefaultTermsDays = input.DefaultTermsDays ?? before.DefaultTermsDays,
            TermsText = input.TermsText is null ? before.TermsText : MoneyText.Clean(input.TermsText),
            Footer = input.Footer is null ? before.Footer : MoneyText.Clean(input.Footer),
        };

        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Invoice prefix", before.InvoicePrefix, after.InvoicePrefix);
        SettingsText.Compare(changes, "Payment terms", $"{before.DefaultTermsDays} days", $"{after.DefaultTermsDays} days");
        SettingsText.Compare(changes, "Printed terms", ProtonText.Cut(before.TermsText, 120), ProtonText.Cut(after.TermsText, 120));
        SettingsText.Compare(changes, "Footer", ProtonText.Cut(before.Footer, 120), ProtonText.Cut(after.Footer, 120));
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new BillingSettingsArgs(domain, after, before),
            new ProtonPreview($"Change the {(domain == DomainCodes.Design ? "Design" : "Execution")} billing settings", changes, Area: domain),
            null);
    }

    protected override object? RequestOf(BillingSettingsArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(BillingSettingsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(args.DomainCode, cancellationToken);
        if (CompanyMoneyText.ChangeOf(current) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The billing settings changed since Proton proposed this. Ask again.");
        }

        // the bank details and reminders come from the database now, never from the proposal
        var c = args.Change;
        var request = new SaveBillingSettingsRequest
        {
            InvoicePrefix = c.InvoicePrefix, DefaultTermsDays = c.DefaultTermsDays, TermsText = c.TermsText, Footer = c.Footer,
            RemindersEnabled = current.RemindersEnabled, ReminderFirstAfterDays = current.ReminderFirstAfterDays,
            ReminderEveryDays = current.ReminderEveryDays, ReminderMax = current.ReminderMax,
            BankAccount = current.BankAccount, BankName = current.BankName, Iban = current.Iban, Swift = current.Swift,
        };
        await context.ValidateAsync(request, cancellationToken);
        await settings.SaveAsync(args.DomainCode, request, cancellationToken);
        return new ProtonActionDone("Updated the billing settings.", "billing_settings", 1, []);
    }
}

public sealed record ExchangeRateArgs(AddExchangeRateRequest Request);

public sealed record ExchangeRateInput : ProtonActionInput
{
    [Description("A currency other than USD, e.g. LBP.")]
    public required string Currency { get; init; }

    [Description("How many units of it one US dollar buys, as the user gave it.")]
    public required decimal RatePerUsd { get; init; }

    [Description("Today or earlier, after the currency's latest rate. Default today.")]
    public DateOnly? EffectiveFrom { get; init; }
}

/// <summary>
/// Adds a dated exchange rate, through POST /api/exchange-rates once confirmed: costs, overhead and invoices in that
/// currency from that day are converted at it every time they are read, so a backdated rate changes past figures.
/// Payments keep the rate they were recorded at. Rates can't be edited or deleted. The user types the rate.
/// </summary>
public sealed class ProposeExchangeRateTool(IBillingSettingsService settings) : ProtonActionTool<ExchangeRateInput, ExchangeRateArgs>
{
    public override string Name => "propose_exchange_rate";

    public override string Description =>
        "Propose a new dated exchange rate for a currency against the US dollar. Costs, overhead and invoices in that " +
        "currency from that day convert at it, so a backdated rate changes past figures; payments keep their own rate. " +
        "It can't be edited or deleted afterwards. The user confirms by typing the rate.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<BillingSettingsController>(nameof(BillingSettingsController.AddExchangeRate));

    protected override async Task<(ExchangeRateArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ExchangeRateInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        MoneyText.RequireTyped(context, input.RatePerUsd);
        var currency = input.Currency.Trim().ToUpperInvariant();
        var latest = (await settings.ListExchangeRatesAsync(new ExchangeRateListQuery { Currency = currency }, cancellationToken))
            .OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
        var request = new AddExchangeRateRequest { Currency = currency, RatePerUsd = input.RatePerUsd, EffectiveFrom = input.EffectiveFrom };
        var from = request.EffectiveFrom ?? context.Today;
        var effect = from < context.Today
            ? $"Backdated: {currency} costs, overhead and invoices from {ProjectText.Date(from)} to today are worked out again at this rate, so past figures change."
            : $"{currency} costs, overhead and invoices from today convert at it.";
        return (new ExchangeRateArgs(request),
            new ProtonPreview($"Set the {currency} rate",
                [
                    new ProtonChange("1 USD", latest is null ? null : $"{MoneyText.Amount(latest.RatePerUsd, currency)} (from {ProjectText.Date(latest.EffectiveFrom)})",
                        MoneyText.Amount(request.RatePerUsd, currency)),
                    new ProtonChange("From", null, ProjectText.Date(from)),
                ],
                $"{effect} Payments keep the rate they were recorded at. A rate can't be edited or deleted: a mistake is corrected by a newer one.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(request.RatePerUsd), Area: "company", Irreversible: true),
            null);
    }

    protected override object RequestOf(ExchangeRateArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ExchangeRateArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var rate = await settings.AddExchangeRateAsync(args.Request, cancellationToken);
        return new ProtonActionDone($"Set 1 USD = {MoneyText.Amount(rate.RatePerUsd, rate.Currency)} from {ProjectText.Date(rate.EffectiveFrom)}.", "exchange_rate", rate.Id, []);
    }
}

/// <param name="Before">The revenue (Design) or the fee (Execution) when proposed.</param>
/// <param name="BeforeMarkup">Execution: the markup when proposed, as stored (a fraction).</param>
public sealed record ProjectMoneyArgs(
    long ProjectId, UpdateProjectPaymentRequest? Payment, UpdateProjectPricingRequest? Pricing, decimal Before, decimal BeforeMarkup = 0);

public sealed record ProjectMoneyInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("Design: the agreed revenue, as the user gave it.")]
    public decimal? Revenue { get; init; }

    [Description("Execution: the default material markup as a percentage, as the user said it: 15 for 15%.")]
    public decimal? MaterialMarkup { get; init; }

    [Description("Why the payment changes, as the user gave it (it goes in the project's payment history). Ask for it if they didn't say.")]
    public required string Reason { get; init; }
}

/// <summary>
/// Sets a Design project's agreed revenue (PUT /api/projects/{id}/payment), or an Execution site's material
/// markup (PUT …/pricing), with why it changes, once confirmed. The user types the new amount.
/// </summary>
public sealed class ProposeProjectMoneyTool(IProjectService projects) : ProtonActionTool<ProjectMoneyInput, ProjectMoneyArgs>
{
    public override string Name => "propose_project_money";

    public override string Description =>
        "Propose a Design project's agreed revenue, or an Execution site's default material markup, with why it changes (a site's price is set by agreeing its BOQ, not here). " +
        "Locked once the project is completed. The user confirms by typing the new amount.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.UpdatePayment));

    private static readonly ProtonEndpoint Pricing = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.UpdatePricing));

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints => [Endpoint, Pricing];

    protected override ProtonEndpoint StepOf(ProjectMoneyArgs args) => args.Pricing is not null ? Pricing : Endpoint;

    protected override async Task<(ProjectMoneyArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ProjectMoneyInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        MoneyText.RequireTyped(context, input.Revenue, input.MaterialMarkup);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var money = project.Money ?? throw new ProtonToolInputException("The user can't see this project's money.");
        if (money.IsLocked)
        {
            throw new ProtonToolInputException("Its money is locked: the project is finished. Reopen it first.");
        }

        var reason = input.Reason.Trim();
        if (reason.Length == 0)
        {
            throw new ProtonToolInputException("Ask the user why the payment changes: it is required.");
        }

        if (project.Domain == DomainCodes.Design)
        {
            var revenue = input.Revenue ?? throw new ProtonToolInputException("Give the new revenue.");
            var payment = new UpdateProjectPaymentRequest { Revenue = revenue, Description = reason };
            return (new ProjectMoneyArgs(project.Id, payment, null, money.Revenue),
                new ProtonPreview($"Set the revenue of {project.Title}",
                    [new ProtonChange("Revenue", MoneyText.Amount(money.Revenue, money.Currency), MoneyText.Amount(revenue, money.Currency)),
                        new ProtonChange("Why", null, reason)],
                    "Its profit and what is left to invoice follow it.",
                    Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(revenue)),
                ProjectText.Ref(project));
        }

        await context.RequireAsync(Pricing);
        if (input.MaterialMarkup is < 0 or > CompanyMoneyText.MaxMarkupPercent)
        {
            throw new ProtonToolInputException($"The markup must be between 0% and {CompanyMoneyText.MaxMarkupPercent}%.");
        }

        var fee = money.ProjectFee ?? 0;
        var markup = money.MaterialMarkup ?? 0;
        // the app keeps the markup as a fraction (0.15); people say it as a percentage (15)
        var pricing = new UpdateProjectPricingRequest
        {
            MaterialMarkup = input.MaterialMarkup is { } percent ? percent / 100 : markup,
            Description = reason,
        };
        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Material markup", CompanyMoneyText.Percent(markup), CompanyMoneyText.Percent(pricing.MaterialMarkup));
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        changes.Add(new ProtonChange("Why", null, reason));

        return (new ProjectMoneyArgs(project.Id, null, pricing, fee, markup),
            new ProtonPreview($"Price {project.Title}", changes, "Materials follow the new markup.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(pricing.MaterialMarkup * 100)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(ProjectMoneyArgs args) => (object?)args.Payment ?? args.Pricing;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProjectMoneyArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        // the pricing request sends the markup again as the card read it: if it changed since, it would be put back
        var current = (await projects.GetAsync(args.ProjectId, cancellationToken)).Money;
        var stale = current is null || (args.Payment is not null
            ? current.Revenue != args.Before
            : (current.ProjectFee ?? 0) != args.Before || (current.MaterialMarkup ?? 0) != args.BeforeMarkup);
        if (stale)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The project's money changed since Proton proposed this. Ask again.");
        }

        if (args.Payment is { } payment)
        {
            var updated = await projects.UpdatePaymentAsync(args.ProjectId, payment, cancellationToken);
            return new ProtonActionDone($"Set the revenue of {updated.Title}.", "project", updated.Id, [ProjectText.Ref(updated)]);
        }

        await context.RequireAsync(Pricing);
        var priced = await projects.UpdatePricingAsync(args.ProjectId, args.Pricing!, cancellationToken);
        return new ProtonActionDone($"Priced {priced.Title}.", "project", priced.Id, [ProjectText.Ref(priced)]);
    }
}

public enum ProtonOverheadStep
{
    AddGroup,
    RenameGroup,
    RemoveGroup,
    Add,
    Update,
    ChangeAmount,
    ChangeCurrency,
    Cancel,
    Resume,
}

public sealed record OverheadArgs(ProtonOverheadStep Step, long? Id, string? Name, long? GroupId, string? Currency, decimal? Amount, DateOnly? FromMonth);

public sealed record OverheadInput : ProtonActionInput
{
    [Description("add_group, rename_group or remove_group (a category); add a company expense, update its name or category, change_amount or change_currency from a month, cancel it, or resume it.")]
    public required ProtonOverheadStep Step { get; init; }

    [Description("The category or expense (from list_overhead); not for add_group or add.")]
    public long? Id { get; init; }

    public string? Name { get; init; }

    [Description("The expense's category.")]
    public long? GroupId { get; init; }

    [Description("USD or LBP.")]
    public string? Currency { get; init; }

    [Description("Per month, as the user gave it.")]
    public decimal? Amount { get; init; }

    [Description("The first month it applies to (any day in it); this month when not given. Never a past month.")]
    public DateOnly? FromMonth { get; init; }
}

/// <summary>
/// The company's recurring expenses and their categories, through the overhead endpoints once confirmed. Anything that
/// sets or changes an amount needs the amount typed; cancelling needs the expense's name.
/// </summary>
public sealed class ProposeOverheadTool(IOverheadService overhead) : ProtonActionTool<OverheadInput, OverheadArgs>
{
    public override string Name => "propose_overhead";

    public override string Description =>
        "Propose a change to the company's recurring expenses (rent, software…): add one, rename or move it, change its " +
        "monthly amount or currency from a month, cancel or resume it, or manage their categories. Amounts are typed to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<OverheadController>(nameof(OverheadController.Create));

    private static readonly ProtonEndpoint Groups = ProtonEndpoint.OfWrite<OverheadController>(nameof(OverheadController.AddGroup));

    // the categories need overhead_category:manage; the expenses themselves overhead:manage (this tool's own endpoint)
    public override IReadOnlyList<ProtonEndpoint> StepEndpoints => [Endpoint, Groups];

    protected override ProtonEndpoint StepOf(OverheadArgs args)
        => args.Step is ProtonOverheadStep.AddGroup or ProtonOverheadStep.RenameGroup or ProtonOverheadStep.RemoveGroup ? Groups : Endpoint;

    protected override async Task<(OverheadArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        OverheadInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var name = MoneyText.Clean(input.Name);
        var currency = input.Currency?.Trim().ToUpperInvariant();
        MoneyText.RequireTyped(context, input.Amount);
        switch (input.Step)
        {
            case ProtonOverheadStep.AddGroup or ProtonOverheadStep.RenameGroup or ProtonOverheadStep.RemoveGroup:
            {
                await context.RequireAsync(Groups);
                var groups = await overhead.GroupsAsync(cancellationToken);
                if (input.Step == ProtonOverheadStep.AddGroup)
                {
                    var newName = name ?? throw new ProtonToolInputException("Give the category's name.");
                    return (new OverheadArgs(input.Step, null, newName, null, null, null, null),
                        new ProtonPreview($"Add the category {newName}", [new ProtonChange("Category", null, newName)], Area: "company"), null);
                }

                var group = groups.FirstOrDefault(g => g.Id == input.Id) ?? throw new NotFoundException("Category not found.");
                if (input.Step == ProtonOverheadStep.RenameGroup)
                {
                    var renamed = name ?? throw new ProtonToolInputException("Give the new name.");
                    return (new OverheadArgs(input.Step, group.Id, renamed, null, null, null, null),
                        new ProtonPreview($"Rename the category {group.Name}", [new ProtonChange("Name", group.Name, renamed)], Area: "company"), null);
                }

                if (group.Lines > 0)
                {
                    throw new ProtonToolInputException($"{group.Name} still has {group.Lines} expense(s): move them to another category first.");
                }

                return (new OverheadArgs(input.Step, group.Id, null, null, null, null, null),
                    new ProtonPreview($"Remove the category {group.Name}", [new ProtonChange("Category", group.Name, "removed")],
                        Confirm: ProtonConfirm.Typed, Phrase: group.Name, Irreversible: true, Area: "company"), null);
            }
            case ProtonOverheadStep.Add:
            {
                var newName = name ?? throw new ProtonToolInputException("Give the expense's name.");
                var amount = input.Amount ?? throw new ProtonToolInputException("Give its monthly amount.");
                var group = (await overhead.GroupsAsync(cancellationToken)).FirstOrDefault(g => g.Id == input.GroupId)
                    ?? throw new ProtonToolInputException("Give its category, from list_overhead.");
                return (new OverheadArgs(input.Step, null, newName, group.Id, currency ?? "USD", amount, input.FromMonth),
                    new ProtonPreview($"Add the company expense {newName}",
                        [
                            new ProtonChange("Expense", null, newName), new ProtonChange("Category", null, group.Name),
                            new ProtonChange("Per month", null, MoneyText.Amount(amount, currency ?? "USD")),
                            new ProtonChange("From", null, OverheadText.Month(input.FromMonth ?? context.Today)),
                        ],
                        Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(amount), Area: "company"), null);
            }
            default:
            {
                var line = (await overhead.GetAsync(cancellationToken)).Lines.FirstOrDefault(l => l.Id == input.Id)
                    ?? throw new NotFoundException("Company expense not found.");
                var now = line.CurrentAmount is { } current ? MoneyText.Amount(current, line.Currency) : "none";
                switch (input.Step)
                {
                    case ProtonOverheadStep.Update:
                        var groupId = input.GroupId ?? line.Group.Id;
                        var group = (await overhead.GroupsAsync(cancellationToken)).FirstOrDefault(g => g.Id == groupId)
                            ?? throw new NotFoundException("Category not found.");
                        var changes = new List<ProtonChange>();
                        SettingsText.Compare(changes, "Name", line.Name, name ?? line.Name);
                        SettingsText.Compare(changes, "Category", line.Group.Name, group.Name);
                        if (changes.Count == 0)
                        {
                            throw new ProtonToolInputException("Nothing would change: say what should be different.");
                        }

                        return (new OverheadArgs(input.Step, line.Id, name ?? line.Name, group.Id, null, null, null),
                            new ProtonPreview($"Change the company expense {line.Name}", changes, Area: "company"), null);
                    case ProtonOverheadStep.ChangeAmount or ProtonOverheadStep.ChangeCurrency:
                        var amount = input.Amount ?? throw new ProtonToolInputException("Give the new monthly amount.");
                        var newCurrency = input.Step == ProtonOverheadStep.ChangeCurrency
                            ? currency ?? throw new ProtonToolInputException("Give the new currency.")
                            : line.Currency;
                        return (new OverheadArgs(input.Step, line.Id, null, null, newCurrency, amount, input.FromMonth),
                            new ProtonPreview($"Change what {line.Name} costs",
                                [
                                    new ProtonChange("Per month", now, MoneyText.Amount(amount, newCurrency)),
                                    new ProtonChange("From", null, OverheadText.Month(input.FromMonth ?? context.Today)),
                                ],
                                "Months before that keep their amount.",
                                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(amount), Area: "company"), null);
                    case ProtonOverheadStep.Cancel:
                        if (line.IsCancelled)
                        {
                            throw new ProtonToolInputException($"{line.Name} is already cancelled.");
                        }

                        return (new OverheadArgs(input.Step, line.Id, null, null, null, null, null),
                            new ProtonPreview($"Cancel the company expense {line.Name}", [new ProtonChange("Per month", now, "stops after this month")],
                                Confirm: ProtonConfirm.Typed, Phrase: line.Name, Area: "company"), null);
                    default:
                        if (!line.IsCancelled)
                        {
                            throw new ProtonToolInputException($"{line.Name} isn't cancelled.");
                        }

                        return (new OverheadArgs(input.Step, line.Id, null, null, null, null, null),
                            new ProtonPreview($"Resume the company expense {line.Name}", [new ProtonChange("State", "Cancelled", "Charged again")], Area: "company"), null);
                }
            }
        }
    }

    protected override object? RequestOf(OverheadArgs args) => args.Step switch
    {
        ProtonOverheadStep.AddGroup or ProtonOverheadStep.RenameGroup => new OverheadGroupRequest { Name = args.Name! },
        ProtonOverheadStep.Add => new CreateOverheadRequest { Name = args.Name!, GroupId = args.GroupId!.Value, Currency = args.Currency!, Amount = args.Amount!.Value, StartMonth = args.FromMonth },
        ProtonOverheadStep.Update => new UpdateOverheadRequest { Name = args.Name!, GroupId = args.GroupId!.Value },
        ProtonOverheadStep.ChangeAmount => new ChangeOverheadAmountRequest { Amount = args.Amount!.Value, FromMonth = args.FromMonth },
        ProtonOverheadStep.ChangeCurrency => new ChangeOverheadCurrencyRequest { Currency = args.Currency!, Amount = args.Amount!.Value, FromMonth = args.FromMonth },
        _ => null,
    };

    protected override async Task<ProtonActionDone> ExecuteAsync(OverheadArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        switch (args.Step)
        {
            case ProtonOverheadStep.AddGroup:
                await context.RequireAsync(Groups);
                var added = await overhead.AddGroupAsync(new OverheadGroupRequest { Name = args.Name! }, cancellationToken);
                return new ProtonActionDone($"Added the category {added.Name}.", "overhead_group", added.Id, []);
            case ProtonOverheadStep.RenameGroup:
                await context.RequireAsync(Groups);
                var renamed = await overhead.RenameGroupAsync(args.Id!.Value, new OverheadGroupRequest { Name = args.Name! }, cancellationToken);
                return new ProtonActionDone($"Renamed the category to {renamed.Name}.", "overhead_group", renamed.Id, []);
            case ProtonOverheadStep.RemoveGroup:
                await context.RequireAsync(Groups);
                await overhead.RemoveGroupAsync(args.Id!.Value, cancellationToken);
                return new ProtonActionDone("Removed the category.", "overhead_group", args.Id.Value, []);
            case ProtonOverheadStep.Add:
                var line = await overhead.CreateAsync((CreateOverheadRequest)RequestOf(args)!, cancellationToken);
                return new ProtonActionDone($"Added {line.Name}.", "overhead", line.Id, []);
            case ProtonOverheadStep.Update:
                var updated = await overhead.UpdateAsync(args.Id!.Value, (UpdateOverheadRequest)RequestOf(args)!, cancellationToken);
                return new ProtonActionDone($"Updated {updated.Name}.", "overhead", updated.Id, []);
            case ProtonOverheadStep.ChangeAmount:
                var amount = await overhead.ChangeAmountAsync(args.Id!.Value, (ChangeOverheadAmountRequest)RequestOf(args)!, cancellationToken);
                return new ProtonActionDone($"Changed what {amount.Name} costs.", "overhead", amount.Id, []);
            case ProtonOverheadStep.ChangeCurrency:
                var moved = await overhead.ChangeCurrencyAsync(args.Id!.Value, (ChangeOverheadCurrencyRequest)RequestOf(args)!, cancellationToken);
                return new ProtonActionDone($"{moved.Name} is now in {moved.Currency}.", "overhead", moved.Id, []);
            case ProtonOverheadStep.Cancel:
                await overhead.CancelAsync(args.Id!.Value, cancellationToken);
                return new ProtonActionDone("Cancelled it.", "overhead", args.Id.Value, []);
            default:
                var resumed = await overhead.ResumeAsync(args.Id!.Value, cancellationToken);
                return new ProtonActionDone($"Resumed {resumed.Name}.", "overhead", resumed.Id, []);
        }
    }
}

internal static class CompanyMoneyText
{
    /// <summary>The highest markup the app takes (its validator's 5, as a fraction).</summary>
    public const decimal MaxMarkupPercent = 500;

    /// <summary>A stored markup (a fraction: 0.15) as people read it ("15%").</summary>
    public static string Percent(decimal fraction) => $"{(fraction * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%";

    public static BillingSettingsChange ChangeOf(BillingSettingsResponse s) => new(s.InvoicePrefix, s.DefaultTermsDays, s.TermsText, s.Footer);
}

internal static class OverheadText
{
    public static string Month(DateOnly day) => day.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
