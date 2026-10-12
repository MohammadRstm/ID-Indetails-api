using Anthropic;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Analysis;
using ID_Indetails.api.Proton.Agent;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
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
        services.AddScoped<IProtonActionRepository, ProtonActionRepository>();
        services.AddScoped<ProtonUsageService>();
        services.AddSingleton<ProtonConversationStore>();
        services.AddScoped<ProtonToolbox>();
        services.AddScoped<ProtonChatAgent>();
        services.AddScoped<ProtonStreamingService>();
        services.AddScoped<ProtonActionService>();

        // Read-only tools, shared by every Proton interface.
        services.AddScoped<IProtonTool, FindTool>();
        services.AddScoped<IProtonTool, ListProjectsTool>();
        services.AddScoped<IProtonTool, GetProjectTool>();
        services.AddScoped<IProtonTool, GetProjectRequirementsTool>();
        services.AddScoped<IProtonTool, GetProjectTeamTool>();
        services.AddScoped<IProtonTool, GetProjectFinancialsTool>();
        services.AddScoped<IProtonTool, GetSiteSummaryTool>();
        services.AddScoped<IProtonTool, ListWorkEntriesTool>();
        services.AddScoped<IProtonTool, GetWorkEntryTool>();
        services.AddScoped<IProtonTool, ListMyWorkTool>();
        services.AddScoped<IProtonTool, ListTeamsTool>();
        services.AddScoped<IProtonTool, GetCompanySettingsTool>();
        services.AddScoped<IProtonTool, GetDomainSettingsTool>();
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

        // Analytical tools: comparisons and signals calculated in code over the existing services, read-only like the rest.
        services.AddScoped<ProtonAnalysisService>();
        services.AddScoped<IProtonTool, CompareWorkTool>();
        services.AddScoped<IProtonTool, CompareMoneyTool>();
        services.AddScoped<IProtonTool, ReviewProjectTool>();
        services.AddScoped<IProtonTool, GetCompanySummaryTool>();
        services.AddScoped<IProtonTool, SummarizeChangesTool>();
        services.AddScoped<IProtonTool, GetAttentionTool>();

        // The user's own chats and notifications, read as them through their services (no message bodies).
        services.AddScoped<IProtonTool, FindRecipientTool>();
        services.AddScoped<IProtonTool, GetMyInboxTool>();
        services.AddScoped<IProtonTool, GetMyNotificationsTool>();
        services.AddScoped<ProtonBriefingService>();

        // UI tools: they read nothing new, they ask the app to open a page or record the user may already open.
        services.AddScoped<ProtonRecords>();
        services.AddScoped<IProtonTool, OpenRecordTool>();
        services.AddScoped<IProtonTool, OpenPageTool>();
        services.AddScoped<IProtonTool, OfferChoicesTool>();
        services.AddScoped<IProtonTool, SuggestRepliesTool>();
        services.AddScoped<IProtonTool, OpenSurfaceTool>();
        services.AddScoped<IProtonTool, FillFormTool>();
        services.AddScoped<IProtonTool, SetPreferenceTool>();
        services.AddScoped<IProtonTool, RefreshPageTool>();

        // Action tools: they only propose; the user's confirmation carries a proposal out (ProtonActionService).
        services.AddScoped<IProtonTool, ProposeAddRequirementTool>();
        services.AddScoped<IProtonTool, ProposeUpdateRequirementTool>();
        services.AddScoped<IProtonTool, ProposeSetRequirementDoneTool>();
        services.AddScoped<IProtonTool, ProposeCreateClientTool>();
        services.AddScoped<IProtonTool, ProposeUpdateClientTool>();
        services.AddScoped<IProtonTool, ProposeSetClientArchivedTool>();
        services.AddScoped<IProtonTool, ProposeScheduleMeetingTool>();
        services.AddScoped<IProtonTool, ProposeUpdateMeetingTool>();
        services.AddScoped<IProtonTool, ProposeCreateTeamTool>();
        services.AddScoped<IProtonTool, ProposeRenameTeamTool>();
        services.AddScoped<IProtonTool, ProposeUpdateMyProfileTool>();
        services.AddScoped<IProtonTool, ProposeCommentWorkEntryTool>();
        services.AddScoped<IProtonTool, ProposeProjectLifecycleTool>();
        services.AddScoped<IProtonTool, ProposeUpdateEmployeeTool>();
        services.AddScoped<IProtonTool, ProposeDeactivateEmployeeTool>();
        services.AddScoped<IProtonTool, ProposeReactivateEmployeeTool>();
        services.AddScoped<IProtonTool, ProposeSessionsTool>();
        services.AddScoped<IProtonTool, ProposeDeleteTeamTool>();
        services.AddScoped<IProtonTool, ProposeCompanySettingsTool>();
        services.AddScoped<IProtonTool, ProposeDomainSettingsTool>();
        services.AddScoped<IProtonTool, ProposeSendMessageTool>();
        services.AddScoped<IProtonTool, ProposeLogWorkTool>();
        services.AddScoped<IProtonTool, ProposeUpdateMyWorkTool>();
        services.AddScoped<IProtonTool, ProposeDeleteMyWorkTool>();
        services.AddScoped<IProtonTool, ProposeChangeProjectStatusTool>();
        services.AddScoped<IProtonTool, ProposeUpdateProjectTool>();
        services.AddScoped<IProtonTool, ProposeCreateProjectTool>();

        return services;
    }
}
