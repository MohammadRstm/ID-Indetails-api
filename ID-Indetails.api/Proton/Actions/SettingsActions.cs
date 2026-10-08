using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Settings;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Settings;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonWeekStart
{
    Sunday,
    Monday,
}

public sealed record CompanySettingsArgs(UpdateCompanySettingsRequest Request, UpdateCompanySettingsRequest Before);

public sealed record CompanySettingsInput : ProtonActionInput
{
    public string? Name { get; init; }

    [Description("For the letterhead of invoices and statements; an empty string removes it.")]
    public string? Address { get; init; }

    [Description("An empty string removes it.")]
    public string? Website { get; init; }

    public ProtonWeekStart? WeekStart { get; init; }

    public bool? Use24HourTime { get; init; }

    [Description("USD or LBP.")]
    public string? DefaultCurrency { get; init; }
}

/// <summary>
/// Changes the company's name, letterhead, week start, time format or default currency, through PUT
/// /api/company/settings once confirmed. Everyone sees the change; a new week start or currency needs the company's
/// name typed to confirm.
/// </summary>
public sealed class ProposeCompanySettingsTool(ICompanySettingsService settings, IEmployeeService employees)
    : ProtonActionTool<CompanySettingsInput, CompanySettingsArgs>
{
    public override string Name => "propose_company_settings";

    public override string Description =>
        "Propose changing the company's name, address, website, week start, time format or default currency (use " +
        "get_company_settings first). It changes what everyone sees. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<CompanyController>(nameof(CompanyController.UpdateSettings));

    protected override async Task<(CompanySettingsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CompanySettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var before = SettingsText.RequestOf(current);
        var after = new UpdateCompanySettingsRequest
        {
            Name = input.Name?.Trim() ?? before.Name,
            Address = input.Address is null ? before.Address : SettingsText.Clean(input.Address),
            Website = input.Website is null ? before.Website : SettingsText.Clean(input.Website),
            WeekStart = input.WeekStart is { } start ? (byte)(start == ProtonWeekStart.Monday ? 1 : 0) : before.WeekStart,
            Use24HourTime = input.Use24HourTime ?? before.Use24HourTime,
            DefaultCurrency = input.DefaultCurrency?.Trim().ToUpperInvariant() ?? before.DefaultCurrency,
        };

        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Name", before.Name, after.Name);
        SettingsText.Compare(changes, "Address", before.Address, after.Address);
        SettingsText.Compare(changes, "Website", before.Website, after.Website);
        SettingsText.Compare(changes, "Week starts on", SettingsText.Week(before.WeekStart), SettingsText.Week(after.WeekStart));
        SettingsText.Compare(changes, "Time format", before.Use24HourTime ? "24-hour" : "12-hour", after.Use24HourTime ? "24-hour" : "12-hour");
        SettingsText.Compare(changes, "Default currency", before.DefaultCurrency, after.DefaultCurrency);
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        // a new week start moves every weekly figure; a new currency changes what new money defaults to
        var typed = after.WeekStart != before.WeekStart || after.DefaultCurrency != before.DefaultCurrency;
        var reach = await SettingsText.ReachAsync(employees, null, cancellationToken);
        return (new CompanySettingsArgs(after, before),
            new ProtonPreview("Change the company settings", changes,
                after.WeekStart != before.WeekStart ? "Weekly figures and reports count from the new first day." : null,
                Confirm: typed ? ProtonConfirm.Typed : ProtonConfirm.TwoStep,
                Phrase: typed ? before.Name : null,
                Area: "company",
                Reach: reach),
            null);
    }

    protected override object RequestOf(CompanySettingsArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CompanySettingsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (SettingsText.RequestOf(await settings.GetAsync(cancellationToken)) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The company settings changed since Proton proposed this. Ask again.");
        }

        await settings.UpdateAsync(args.Request, cancellationToken);
        return new ProtonActionDone("Updated the company settings.", "company_settings", 1, []);
    }
}

public sealed record DomainSettingsArgs(string DomainCode, SaveDomainSettingsRequest Request, SaveDomainSettingsRequest Before);

public sealed record DomainSettingsInput : ProtonActionInput
{
    [Description("design or execution.")]
    public required string Domain { get; init; }

    [Description("How long people can still edit their own entries.")]
    public EditWindow? EditWindow { get; init; }

    public bool? AllowOverlap { get; init; }

    public bool? AllowFutureEntries { get; init; }

    public bool? RequireDescription { get; init; }

    public bool? TimerEnabled { get; init; }

    public bool? LogReminder { get; init; }

    public EntryNotificationMode? NotifyEntry { get; init; }

    public bool? NotifyProject { get; init; }

    public bool? NotifyIdle { get; init; }

    public bool? WeeklySummary { get; init; }

    [Description("Stops everyone in the domain from logging work while on.")]
    public bool? HoldLogging { get; init; }

    public bool? AutoArchive { get; init; }
}

/// <summary>
/// Changes a domain's work-logging rules and notifications, through PUT /api/domain-settings/{domain} once confirmed. The
/// card says how many people it affects; holding or releasing logging needs the domain's name typed to confirm.
/// </summary>
public sealed class ProposeDomainSettingsTool(IDomainSettingsService settings, IEmployeeService employees)
    : ProtonActionTool<DomainSettingsInput, DomainSettingsArgs>
{
    public override string Name => "propose_domain_settings";

    public override string Description =>
        "Propose changing a domain's work-logging rules (edit window, overlaps, future entries, descriptions, the timer, " +
        "holding all logging) or its notifications (use get_domain_settings first). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<DomainSettingsController>(nameof(DomainSettingsController.Save));

    protected override async Task<(DomainSettingsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DomainSettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = ClientText.DomainOf(input.Domain, context);
        var current = await settings.GetAsync(domain, cancellationToken);
        var before = SettingsText.RequestOf(current);
        var after = new SaveDomainSettingsRequest
        {
            EditWindow = input.EditWindow ?? before.EditWindow,
            AllowOverlap = input.AllowOverlap ?? before.AllowOverlap,
            AllowFutureEntries = input.AllowFutureEntries ?? before.AllowFutureEntries,
            RequireDescription = input.RequireDescription ?? before.RequireDescription,
            TimerEnabled = input.TimerEnabled ?? before.TimerEnabled,
            LogReminder = input.LogReminder ?? before.LogReminder,
            NotifyEntry = input.NotifyEntry ?? before.NotifyEntry,
            NotifyProject = input.NotifyProject ?? before.NotifyProject,
            NotifyIdle = input.NotifyIdle ?? before.NotifyIdle,
            WeeklySummary = input.WeeklySummary ?? before.WeeklySummary,
            HoldLogging = input.HoldLogging ?? before.HoldLogging,
            AutoArchive = input.AutoArchive ?? before.AutoArchive,
        };

        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Edit window", SettingsText.Words(before.EditWindow), SettingsText.Words(after.EditWindow));
        SettingsText.Compare(changes, "Overlapping entries", SettingsText.OnOff(before.AllowOverlap), SettingsText.OnOff(after.AllowOverlap));
        SettingsText.Compare(changes, "Future entries", SettingsText.OnOff(before.AllowFutureEntries), SettingsText.OnOff(after.AllowFutureEntries));
        SettingsText.Compare(changes, "Description required", SettingsText.OnOff(before.RequireDescription), SettingsText.OnOff(after.RequireDescription));
        SettingsText.Compare(changes, "Timer", SettingsText.OnOff(before.TimerEnabled), SettingsText.OnOff(after.TimerEnabled));
        SettingsText.Compare(changes, "Log reminder", SettingsText.OnOff(before.LogReminder), SettingsText.OnOff(after.LogReminder));
        SettingsText.Compare(changes, "Entry notifications", SettingsText.Words(before.NotifyEntry), SettingsText.Words(after.NotifyEntry));
        SettingsText.Compare(changes, "Project notifications", SettingsText.OnOff(before.NotifyProject), SettingsText.OnOff(after.NotifyProject));
        SettingsText.Compare(changes, "Idle notifications", SettingsText.OnOff(before.NotifyIdle), SettingsText.OnOff(after.NotifyIdle));
        SettingsText.Compare(changes, "Weekly summary", SettingsText.OnOff(before.WeeklySummary), SettingsText.OnOff(after.WeeklySummary));
        SettingsText.Compare(changes, "Logging on hold", SettingsText.OnOff(before.HoldLogging), SettingsText.OnOff(after.HoldLogging));
        SettingsText.Compare(changes, "Auto-archive", SettingsText.OnOff(before.AutoArchive), SettingsText.OnOff(after.AutoArchive));
        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        var area = domain == DomainCodes.Design ? "Design" : "Execution";
        var holds = after.HoldLogging != before.HoldLogging;
        return (new DomainSettingsArgs(domain, after, before),
            new ProtonPreview($"Change the {area} settings", changes,
                holds && after.HoldLogging ? $"Nobody in {area} can log work until logging is released." : null,
                Confirm: holds ? ProtonConfirm.Typed : ProtonConfirm.TwoStep,
                Phrase: holds ? area : null,
                Area: domain,
                Reach: await SettingsText.ReachAsync(employees, domain, cancellationToken)),
            null);
    }

    protected override object RequestOf(DomainSettingsArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(DomainSettingsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (SettingsText.RequestOf(await settings.GetAsync(args.DomainCode, cancellationToken)) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The settings changed since Proton proposed this. Ask again.");
        }

        await settings.SaveAsync(args.DomainCode, args.Request, cancellationToken);
        return new ProtonActionDone($"Updated the {(args.DomainCode == DomainCodes.Design ? "Design" : "Execution")} settings.", "domain_settings", 1, []);
    }
}

internal static class SettingsText
{
    public static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static void Compare(List<ProtonChange> changes, string label, string? was, string? now)
    {
        if (was != now)
        {
            changes.Add(new ProtonChange(label, was ?? "none", now ?? "none"));
        }
    }

    public static string Week(byte start) => start == 0 ? "Sunday" : "Monday";

    public static string OnOff(bool on) => on ? "On" : "Off";

    public static string Words<T>(T value) where T : struct, Enum
        => string.Concat(value.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : c.ToString()));

    public static UpdateCompanySettingsRequest RequestOf(CompanySettingsResponse s) => new()
    {
        Name = s.Name, Address = s.Address, Website = s.Website, WeekStart = s.WeekStart, Use24HourTime = s.Use24HourTime, DefaultCurrency = s.DefaultCurrency,
    };

    public static SaveDomainSettingsRequest RequestOf(DomainSettingsResponse s) => new()
    {
        EditWindow = s.EditWindow, AllowOverlap = s.AllowOverlap, AllowFutureEntries = s.AllowFutureEntries, RequireDescription = s.RequireDescription,
        TimerEnabled = s.TimerEnabled, LogReminder = s.LogReminder, NotifyEntry = s.NotifyEntry, NotifyProject = s.NotifyProject,
        NotifyIdle = s.NotifyIdle, WeeklySummary = s.WeeklySummary, HoldLogging = s.HoldLogging, AutoArchive = s.AutoArchive,
    };

    /// <summary>Who the change reaches, counted on the server: the active people of the domain, or of the company.</summary>
    public static async Task<string> ReachAsync(IEmployeeService employees, string? domain, CancellationToken cancellationToken)
    {
        var people = await employees.ListAsync(new EmployeeListQuery { Domain = domain, Status = UserStatus.Active, PageSize = 1 }, cancellationToken);
        var where = domain is null ? "the company" : domain == DomainCodes.Design ? "Design" : "Execution";
        return $"Affects {people.TotalCount} active {(people.TotalCount == 1 ? "person" : "people")} in {where}.";
    }
}
