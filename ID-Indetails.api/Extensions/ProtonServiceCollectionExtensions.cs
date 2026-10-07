using Anthropic;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Agent;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Repositories.Proton.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Extensions;

public static class ProtonServiceCollectionExtensions
{
    /// <summary>Proton: its settings, the Claude client, the read-only tools, conversations and usage.</summary>
    public static IServiceCollection AddProton(this IServiceCollection services)
    {
        services.AddOptions<ProtonOptions>().BindConfiguration(ProtonOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.ApiKey), "Proton:ApiKey is required when Proton is enabled.")
            .ValidateOnStart();

        // One client for the app: it holds the HTTP connection pool. The SDK's own retry covers one transient failure.
        // Without a key Proton is off and the client is never called; the placeholder only lets it be constructed.
        services.AddSingleton<IAnthropicClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ProtonOptions>>().Value;
            return new AnthropicClient
            {
                ApiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? "not-configured" : options.ApiKey,
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(options.ModelTimeoutSeconds),
            };
        });

        services.AddScoped<IProtonUsageRepository, ProtonUsageRepository>();
        services.AddScoped<ProtonUsageService>();
        services.AddSingleton<ProtonConversationStore>();
        services.AddScoped<ProtonToolbox>();
        services.AddScoped<ProtonChatAgent>();

        // Read-only tools, shared by every Proton interface.
        services.AddScoped<IProtonTool, FindTool>();
        services.AddScoped<IProtonTool, ListProjectsTool>();
        services.AddScoped<IProtonTool, GetProjectTool>();
        services.AddScoped<IProtonTool, GetProjectRequirementsTool>();
        services.AddScoped<IProtonTool, GetProjectTeamTool>();
        services.AddScoped<IProtonTool, GetProjectFinancialsTool>();
        services.AddScoped<IProtonTool, GetSiteSummaryTool>();
        services.AddScoped<IProtonTool, ListWorkEntriesTool>();
        services.AddScoped<IProtonTool, GetTimeSummaryTool>();
        services.AddScoped<IProtonTool, GetWorkOverviewTool>();
        services.AddScoped<IProtonTool, GetPersonTool>();
        services.AddScoped<IProtonTool, GetPersonWorkTool>();
        services.AddScoped<IProtonTool, ListPeopleActivityTool>();
        services.AddScoped<IProtonTool, GetRecentActivityTool>();
        services.AddScoped<IProtonTool, ListMeetingsTool>();
        services.AddScoped<IProtonTool, GetDomainDashboardTool>();
        services.AddScoped<IProtonTool, ListClientsTool>();
        services.AddScoped<IProtonTool, GetClientTool>();
        services.AddScoped<IProtonTool, GetDomainFinanceTool>();
        services.AddScoped<IProtonTool, GetFinanceProjectsTool>();
        services.AddScoped<IProtonTool, GetMoneyAttentionTool>();
        services.AddScoped<IProtonTool, ListInvoicesTool>();
        services.AddScoped<IProtonTool, ListPaymentsTool>();
        services.AddScoped<IProtonTool, GetCompanyOverviewTool>();

        return services;
    }
}
