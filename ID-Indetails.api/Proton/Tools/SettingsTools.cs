using System.ComponentModel;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record NoSettingsInput;

public sealed record CompanySettingsRow(string Name, string? Address, string? Website, string WeekStartsOn, bool Use24HourTime, string DefaultCurrency, string TimeZone);

/// <summary>The company's name, letterhead and conventions, as GET /api/company/settings returns them (without the logo).</summary>
public sealed class GetCompanySettingsTool(ICompanySettingsService settings) : ProtonTool<NoSettingsInput>
{
    public override string Name => "get_company_settings";

    public override string Description => "The company's name, address, website, week start, time format, default currency and time zone.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<CompanyController>(nameof(CompanyController.GetSettings));

    protected override async Task<ProtonToolOutput> RunAsync(NoSettingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var s = await settings.GetAsync(cancellationToken);
        return ProtonToolOutput.Of(new CompanySettingsRow(s.Name, s.Address, s.Website, s.WeekStart == 0 ? "Sunday" : "Monday", s.Use24HourTime, s.DefaultCurrency, s.TimeZone));
    }
}

public sealed record DomainSettingsInputQuery
{
    [Description("design or execution.")]
    public required string Domain { get; init; }
}

/// <summary>A domain's work-logging rules and notifications, as GET /api/domain-settings/{domain} returns them.</summary>
public sealed class GetDomainSettingsTool(IDomainSettingsService settings) : ProtonTool<DomainSettingsInputQuery>
{
    public override string Name => "get_domain_settings";

    public override string Description =>
        "A domain's work-logging rules (edit window, overlaps, future entries, descriptions, timer, logging on hold) and its notifications.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBased<DomainSettingsController>(nameof(DomainSettingsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(DomainSettingsInputQuery input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = input.Domain.Trim().ToLowerInvariant();
        if (!context.Access.VisibleDomains.Contains(domain))
        {
            throw new ProtonToolInputException($"The user can't see {input.Domain}.");
        }

        return ProtonToolOutput.Of(await settings.GetAsync(domain, cancellationToken));
    }
}
