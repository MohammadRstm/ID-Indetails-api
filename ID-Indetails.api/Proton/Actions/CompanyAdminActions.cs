using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonCompanySwitch
{
    ReplySuggestions,
    ProjectFiles,
}

/// <param name="Before">The switch when proposed: if it changed since, the change is refused.</param>
public sealed record CompanyProtonArgs(ProtonCompanySwitch Setting, CompanyProtonSwitchRequest Request, bool Before);

public sealed record CompanyProtonInput : ProtonActionInput
{
    [Description("reply_suggestions (Proton suggests replies in the discussions with clients, for everyone) or project_files (Proton may read project files).")]
    public required ProtonCompanySwitch Setting { get; init; }

    public required bool Enabled { get; init; }
}

/// <summary>
/// Switches one of Proton's company-wide settings, through PUT /api/company/proton-suggestions or /proton-files once
/// confirmed. Reading project files sends their content to Anthropic, Proton's model provider. The company's name is
/// typed to confirm.
/// </summary>
public sealed class ProposeCompanyProtonTool(ICompanySettingsService settings, IEmployeeService employees)
    : ProtonActionTool<CompanyProtonInput, CompanyProtonArgs>
{
    public override string Name => "propose_company_proton";

    public override string Description =>
        "Propose switching one of Proton's own company-wide settings: whether it suggests replies in the discussions with " +
        "clients for everyone, or whether it may read project files (which sends their content to Anthropic, its model " +
        "provider). The user confirms by typing the company's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // both switches share their policy
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<CompanyController>(nameof(CompanyController.SetProtonFiles));

    protected override async Task<(CompanyProtonArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CompanyProtonInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var before = CompanyAdminText.Switch(current, input.Setting);
        var files = input.Setting == ProtonCompanySwitch.ProjectFiles;
        if (before == input.Enabled)
        {
            throw new ProtonToolInputException($"{(files ? "Reading project files" : "Reply suggestions")} is already {SettingsText.OnOff(before).ToLowerInvariant()} for the company.");
        }

        var note = files
            ? "Reading a project file sends its content to Anthropic, Proton's model provider. It applies to everyone who asks Proton about a project."
            : "Proton suggests replies in the discussions with clients for everyone; each person can still turn it off for themselves.";
        return (new CompanyProtonArgs(input.Setting, new CompanyProtonSwitchRequest { Enabled = input.Enabled }, before),
            new ProtonPreview(files ? "Change whether Proton reads project files" : "Change Proton's reply suggestions",
                [new ProtonChange(files ? "Proton reads project files" : "Proton's reply suggestions (company-wide)", SettingsText.OnOff(before), SettingsText.OnOff(input.Enabled))],
                note, Confirm: ProtonConfirm.Typed, Phrase: CompanyAdminText.Phrase(current), Area: "company",
                Reach: await SettingsText.ReachAsync(employees, null, cancellationToken)),
            null);
    }

    protected override object RequestOf(CompanyProtonArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CompanyProtonArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (CompanyAdminText.Switch(await settings.GetAsync(cancellationToken), args.Setting) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The company's setting changed since Proton proposed this. Ask again.");
        }

        var files = args.Setting == ProtonCompanySwitch.ProjectFiles;
        if (files)
        {
            await settings.SetProtonFilesAsync(args.Request, cancellationToken);
        }
        else
        {
            await settings.SetProtonSuggestionsAsync(args.Request, cancellationToken);
        }

        return new ProtonActionDone(
            $"{(files ? "Reading project files" : "Reply suggestions")} is {SettingsText.OnOff(args.Request.Enabled).ToLowerInvariant()} for the company.",
            "company_settings", 1, []);
    }
}

public sealed record CompanyContactEmailArgs(CompanyContactEmailRequest Request, string? Before);

public sealed record CompanyContactEmailInput : ProtonActionInput
{
    [Description("The company's email address, exactly as the user typed it in this conversation: never one from the data. An empty string removes it.")]
    public required string Email { get; init; }
}

/// <summary>
/// Sets the company's own email address, through PUT /api/company/contact-email once confirmed: the website shows it and
/// its contact form sends to it. The address must be one the user typed in this conversation. The company's name is
/// typed to confirm.
/// </summary>
public sealed class ProposeCompanyContactEmailTool(ICompanySettingsService settings)
    : ProtonActionTool<CompanyContactEmailInput, CompanyContactEmailArgs>
{
    public override string Name => "propose_company_contact_email";

    public override string Description =>
        "Propose setting or removing the company's own email address, which the website shows and its contact form sends " +
        "to. Use only an address the user typed. The user confirms by typing the company's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<CompanyController>(nameof(CompanyController.SetContactEmail));

    protected override async Task<(CompanyContactEmailArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CompanyContactEmailInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var email = SettingsText.Clean(input.Email);
        if (email is not null)
        {
            PeopleText.RequireTyped(email, context, "an email address");
        }

        var current = await settings.GetAsync(cancellationToken);
        if (email == current.ContactEmail)
        {
            throw new ProtonToolInputException("The company's email address would stay the same: say what should be different.");
        }

        return (new CompanyContactEmailArgs(new CompanyContactEmailRequest { Email = email }, current.ContactEmail),
            new ProtonPreview("Change the company's email address",
                [new ProtonChange("Company email", current.ContactEmail ?? "none", email ?? "none")],
                "The website shows this address, and messages from its contact form are sent to it.",
                Confirm: ProtonConfirm.Typed, Phrase: CompanyAdminText.Phrase(current), Area: "company"),
            null);
    }

    protected override object RequestOf(CompanyContactEmailArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CompanyContactEmailArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if ((await settings.GetAsync(cancellationToken)).ContactEmail != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The company's email address changed since Proton proposed this. Ask again.");
        }

        var saved = await settings.SetContactEmailAsync(args.Request, cancellationToken);
        return new ProtonActionDone(saved.ContactEmail is null ? "Removed the company's email address." : $"The company's email address is now {saved.ContactEmail}.", "company_settings", 1, []);
    }
}

public sealed record RemoveLogoArgs(long LogoFileId);

public sealed record RemoveLogoInput : ProtonActionInput;

/// <summary>Removes the company logo, through DELETE /api/company/logo once confirmed. A new one comes from a picture on a person's device, in the app.</summary>
public sealed class ProposeRemoveCompanyLogoTool(ICompanySettingsService settings) : ProtonActionTool<RemoveLogoInput, RemoveLogoArgs>
{
    public override string Name => "propose_remove_company_logo";

    public override string Description =>
        "Propose removing the company's logo (a new one needs a picture from a device: open the company settings for " +
        "that). The user confirms by typing the company's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<CompanyController>(nameof(CompanyController.RemoveLogo));

    protected override async Task<(RemoveLogoArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RemoveLogoInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var logo = current.LogoFileId ?? throw new ProtonToolInputException("The company has no logo.");
        return (new RemoveLogoArgs(logo),
            new ProtonPreview("Remove the company logo", [new ProtonChange("Company logo", "set", "removed")],
                "The sign-in page and the documents the company prints show no logo until a new one is uploaded.",
                Confirm: ProtonConfirm.Typed, Phrase: CompanyAdminText.Phrase(current), Area: "company", Irreversible: true),
            null);
    }

    protected override object? RequestOf(RemoveLogoArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(RemoveLogoArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if ((await settings.GetAsync(cancellationToken)).LogoFileId != args.LogoFileId)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The company's logo changed since Proton proposed this. Ask again.");
        }

        await settings.RemoveLogoAsync(cancellationToken);
        return new ProtonActionDone("Removed the company logo.", "company_settings", 1, []);
    }
}

public enum ProtonLabelStep
{
    Create,
    Rename,
    Archive,
    Restore,
    Order,
    Delete,
}

/// <param name="Before">The labels as proposed (<see cref="CompanyAdminText.Signature"/>): if any changed since, the change is refused.</param>
public sealed record AccountLabelArgs(ProtonLabelStep Step, long? Id, string Before, AccountLabelRequest? Name, AccountLabelOrderRequest? Order);

public sealed record AccountLabelInput : ProtonActionInput
{
    [Description("create, rename, archive (it can no longer be picked; what carries it keeps it), restore, order (the pick list), or delete (only a label nothing is filed under).")]
    public required ProtonLabelStep Step { get; init; }

    [Description("create: the new label's name. rename, archive, restore, delete: the existing label, by name.")]
    public string? Name { get; init; }

    [Description("rename: the new name.")]
    public string? NewName { get; init; }

    [Description("order: the labels to put first, in this order, by name; the others follow in their present order.")]
    public IReadOnlyList<string>? First { get; init; }
}

/// <summary>
/// The company's account labels (what payments and expenses are filed under), through the account-label endpoints once
/// confirmed. Labels are found by name. Renaming, archiving and deleting need the label's name typed; adding, restoring
/// and ordering need the second step.
/// </summary>
public sealed class ProposeAccountLabelTool(IAccountLabelService labels) : ProtonActionTool<AccountLabelInput, AccountLabelArgs>
{
    public override string Name => "propose_account_label";

    public override string Description =>
        "Propose a change to the company's account labels (what payments and site expenses are filed under): add one, " +
        "rename it, archive it (it can no longer be picked; what already carries it keeps it), restore it, reorder the " +
        "pick list, or delete one nothing is filed under (a label in use is archived instead). Labels are named, not numbered. " +
        "Renaming, archiving and deleting are confirmed by typing the label's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // add, rename, archive, restore, order and delete share one policy
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<AccountLabelsController>(nameof(AccountLabelsController.Create));

    protected override async Task<(AccountLabelArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        AccountLabelInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var all = await labels.ListAsync(true, cancellationToken);
        var before = CompanyAdminText.Signature(all);
        var active = all.Where(l => !l.Archived).OrderBy(l => l.Position).ToList();
        switch (input.Step)
        {
            case ProtonLabelStep.Create:
            {
                var name = SettingsText.Clean(input.Name ?? string.Empty) ?? throw new ProtonToolInputException("Give the new label's name.");
                CompanyAdminText.RequireFree(all, name, null);
                return (new AccountLabelArgs(input.Step, null, before, new AccountLabelRequest { Name = name }, null),
                    new ProtonPreview($"Add the account label {name}", [new ProtonChange("Account label", null, name), new ProtonChange("Position", null, "last")],
                        "Payments and expenses can be filed under it from then on.", Area: "company"),
                    null);
            }
            case ProtonLabelStep.Order:
            {
                var first = (input.First ?? []).Select(n => CompanyAdminText.Find(active, n)).DistinctBy(l => l.Id).ToList();
                if (first.Count == 0)
                {
                    throw new ProtonToolInputException("Name the labels to put first, in the order wanted.");
                }

                var order = first.Concat(active.Where(l => first.All(f => f.Id != l.Id))).ToList();
                if (order.Select(l => l.Id).SequenceEqual(active.Select(l => l.Id)))
                {
                    throw new ProtonToolInputException("The labels are already in that order.");
                }

                return (new AccountLabelArgs(input.Step, null, before, null, new AccountLabelOrderRequest { Ids = order.Select(l => l.Id).ToList() }),
                    new ProtonPreview("Reorder the account labels",
                        [new ProtonChange("Pick list", string.Join(", ", active.Select(l => l.Name)), string.Join(", ", order.Select(l => l.Name)))],
                        "Archived labels stay out of the list.", Area: "company"),
                    null);
            }
        }

        var label = CompanyAdminText.Find(all, input.Name ?? string.Empty);
        switch (input.Step)
        {
            case ProtonLabelStep.Rename:
            {
                var renamed = SettingsText.Clean(input.NewName ?? string.Empty) ?? throw new ProtonToolInputException("Give the new name.");
                if (renamed == label.Name)
                {
                    throw new ProtonToolInputException("The label already has that name.");
                }

                CompanyAdminText.RequireFree(all, renamed, label.Id);
                return (new AccountLabelArgs(input.Step, label.Id, before, new AccountLabelRequest { Name = renamed }, null),
                    new ProtonPreview($"Rename the account label {label.Name}", [new ProtonChange("Name", label.Name, renamed)],
                        "Every payment and expense filed under it shows the new name.",
                        Confirm: ProtonConfirm.Typed, Phrase: label.Name, Area: "company"),
                    null);
            }
            case ProtonLabelStep.Archive:
                if (label.Archived)
                {
                    throw new ProtonToolInputException($"{label.Name} is already archived.");
                }

                return (new AccountLabelArgs(input.Step, label.Id, before, null, null),
                    new ProtonPreview($"Archive the account label {label.Name}", [new ProtonChange("State", "In use", "Archived")],
                        "It can no longer be picked for new payments and expenses; what already carries it keeps it. It can be restored.",
                        Confirm: ProtonConfirm.Typed, Phrase: label.Name, Area: "company"),
                    null);
            case ProtonLabelStep.Delete:
                if (label.InUse)
                {
                    throw new ProtonToolInputException($"Payments or expenses are filed under {label.Name}, so it can't be deleted: archive it instead.");
                }

                return (new AccountLabelArgs(input.Step, label.Id, before, null, null),
                    new ProtonPreview($"Delete the account label {label.Name}", [new ProtonChange("Account label", label.Name, null)],
                        "Nothing is filed under it. It is removed for good; a label with the same name can be added again later.",
                        Confirm: ProtonConfirm.Typed, Phrase: label.Name, Area: "company", Irreversible: true),
                    null);
            default:
                if (!label.Archived)
                {
                    throw new ProtonToolInputException($"{label.Name} isn't archived.");
                }

                return (new AccountLabelArgs(input.Step, label.Id, before, null, null),
                    new ProtonPreview($"Restore the account label {label.Name}", [new ProtonChange("State", "Archived", "In use, last in the list")],
                        "It can be picked again for new payments and expenses.", Area: "company"),
                    null);
        }
    }

    protected override object? RequestOf(AccountLabelArgs args) => (object?)args.Name ?? args.Order;

    protected override async Task<ProtonActionDone> ExecuteAsync(AccountLabelArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (CompanyAdminText.Signature(await labels.ListAsync(true, cancellationToken)) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The account labels changed since Proton proposed this. Ask again.");
        }

        switch (args.Step)
        {
            case ProtonLabelStep.Create:
                var added = await labels.AddAsync(args.Name!, cancellationToken);
                return new ProtonActionDone($"Added the account label {added.Name}.", "account_label", added.Id, []);
            case ProtonLabelStep.Rename:
                var renamed = await labels.RenameAsync(args.Id!.Value, args.Name!, cancellationToken);
                return new ProtonActionDone($"Renamed the account label to {renamed.Name}.", "account_label", renamed.Id, []);
            case ProtonLabelStep.Archive:
                var archived = await labels.ArchiveAsync(args.Id!.Value, cancellationToken);
                return new ProtonActionDone($"Archived the account label {archived.Name}.", "account_label", archived.Id, []);
            case ProtonLabelStep.Restore:
                var restored = await labels.RestoreAsync(args.Id!.Value, cancellationToken);
                return new ProtonActionDone($"Restored the account label {restored.Name}.", "account_label", restored.Id, []);
            case ProtonLabelStep.Delete:
                await labels.DeleteAsync(args.Id!.Value, cancellationToken);
                return new ProtonActionDone("Deleted the account label.", "account_label", args.Id.Value, []);
            default:
                await labels.OrderAsync(args.Order!, cancellationToken);
                return new ProtonActionDone("Reordered the account labels.", "account_label", 0, []);
        }
    }
}

public sealed record ProtonTopUpArgs(TopUpProtonBudgetRequest Request);

public sealed record ProtonTopUpInput : ProtonActionInput
{
    [Description("US dollars to add to this month's Proton budget, exactly as the user typed it (at most 10,000).")]
    public required decimal AmountUsd { get; init; }

    [Description("A short note kept with the top-up, such as why.")]
    public string? Note { get; init; }
}

/// <summary>
/// Adds to this month's Proton budget, through POST /api/proton/budget/top-ups once confirmed. It is kept, never edited or
/// taken back. The amount must be one the user typed in this conversation, and is typed again to confirm.
/// </summary>
public sealed class ProposeProtonTopUpTool(ProtonUsageService usage) : ProtonActionTool<ProtonTopUpInput, ProtonTopUpArgs>
{
    public override string Name => "propose_proton_top_up";

    public override string Description =>
        "Propose adding to this month's Proton budget (what Proton may spend on its model), in US dollars. It is kept and " +
        "can't be taken back. Use only an amount the user typed. The user confirms by typing the amount.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProtonController>(nameof(ProtonController.TopUp));

    protected override async Task<(ProtonTopUpArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ProtonTopUpInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        MoneyText.RequireTyped(context, input.AmountUsd);
        var budget = await usage.BudgetAsync(cancellationToken);
        var request = new TopUpProtonBudgetRequest { AmountUsd = input.AmountUsd, Note = SettingsText.Clean(input.Note ?? string.Empty) };
        var changes = new List<ProtonChange>
        {
            new($"Budget for {OverheadText.Month(budget.Month)}", MoneyText.Amount(budget.BudgetUsd, "USD"), MoneyText.Amount(budget.BudgetUsd + request.AmountUsd, "USD")),
            new("Left to spend", MoneyText.Amount(budget.RemainingUsd, "USD"), MoneyText.Amount(budget.RemainingUsd + request.AmountUsd, "USD")),
        };
        if (request.Note is not null)
        {
            changes.Add(new ProtonChange("Note", null, request.Note));
        }

        return (new ProtonTopUpArgs(request),
            new ProtonPreview("Top up Proton's budget", changes,
                "It is added to this month's budget and kept: a top-up can't be edited or taken back. Proton spends it as people ask questions.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(request.AmountUsd), Area: "company", Irreversible: true),
            null);
    }

    protected override object RequestOf(ProtonTopUpArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProtonTopUpArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var budget = await usage.TopUpAsync(context.Access.UserId, args.Request, cancellationToken);
        return new ProtonActionDone(
            $"Added {MoneyText.Amount(args.Request.AmountUsd, "USD")} to Proton's budget for {OverheadText.Month(budget.Month)}.",
            "proton_top_up", budget.TopUps.Select(t => t.Id).DefaultIfEmpty(0).Max(), []);
    }
}

internal static class CompanyAdminText
{
    /// <summary>What the user types to confirm a company-wide change: the company's name (its default one until it is set up).</summary>
    public static string Phrase(CompanySettingsResponse settings) => string.IsNullOrWhiteSpace(settings.Name) ? "company" : settings.Name;

    public static bool Switch(CompanySettingsResponse settings, ProtonCompanySwitch setting)
        => setting == ProtonCompanySwitch.ProjectFiles ? settings.ProtonReadsFiles : settings.ProtonReplySuggestions;

    /// <summary>The labels and their order: if any of it differs at Confirm, a label changed since the card.</summary>
    public static string Signature(IEnumerable<AccountLabelResponse> labels)
        => string.Join(';', labels.OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.Name}:{l.Archived}:{l.Position}"));

    /// <summary>A label by name, whatever its case; the labels that exist are named back when it isn't one.</summary>
    public static AccountLabelResponse Find(IReadOnlyList<AccountLabelResponse> labels, string name)
        => labels.FirstOrDefault(l => string.Equals(l.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new ProtonToolInputException(
               $"There is no account label called \"{name.Trim()}\" here. The labels: {string.Join(", ", labels.OrderBy(l => l.Position).Select(l => l.Archived ? $"{l.Name} (archived)" : l.Name))}.");

    /// <summary>Names are unique whatever their case, archived labels included.</summary>
    public static void RequireFree(IReadOnlyList<AccountLabelResponse> labels, string name, long? exceptId)
    {
        if (labels.Any(l => l.Id != exceptId && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ProtonToolInputException($"There is already an account label called \"{name}\" (archived ones count too).");
        }
    }
}
