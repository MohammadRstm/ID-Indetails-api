using Anthropic;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Proton.Actions;
using ID_Indetails.api.Proton.Analysis;
using ID_Indetails.api.Proton.Agent;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Files;
using ID_Indetails.api.Proton.Suggestions;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Proton.Usage;
using ID_Indetails.api.Proton.Voice;
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
            .Validate(o => !o.Voice.Enabled || !string.IsNullOrWhiteSpace(o.Voice.ApiKey), "Proton:Voice:ApiKey is required when voice is enabled.")
            .Validate(o => Uri.TryCreate(o.Voice.BaseUrl, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.IsLoopback),
                "Proton:Voice:BaseUrl must be an https URL.")
            .Validate(o => o.History.RetentionDays is >= 1 and <= 3650, "Proton:History:RetentionDays must be between 1 and 3650.")
            .Validate(o => o.Visuals.MaxBlocksPerAnswer is >= 1 and <= 10 && o.Visuals.DatasetMinutes is >= 1 and <= 240, "Proton:Visuals limits are out of range.")
            .Validate(o => o.Voice.MaxSessionMinutes is >= 1 and <= 60 && o.Voice.PricePerMinuteUsd >= 0 && o.Voice.CloseWaitSeconds is >= 1 and <= 120,
                "Proton:Voice limits are out of range.")
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
        services.AddScoped<IProtonMemoryRepository, ProtonMemoryRepository>();
        services.AddScoped<IProtonHistoryRepository, ProtonHistoryRepository>();
        services.AddScoped<Proton.History.ProtonHistoryService>();
        services.AddHostedService<Jobs.ProtonHistoryCleanupJob>();
        services.AddScoped<Proton.Memory.ProtonMemoryService>();
        services.AddScoped<ProtonUsageService>();
        services.AddScoped<ProtonReplySuggester>();
        services.AddSingleton<ProtonBudgetHolds>();
        services.AddSingleton<ProtonConversationStore>();
        services.AddScoped<ProtonToolbox>();
        services.AddScoped<ProtonChatAgent>();
        services.AddScoped<ProtonStreamingService>();
        services.AddScoped<ProtonActionService>();

        // Voice: the live model listens and speaks; its questions go to the agent above. The key stays on the server.
        services.AddHttpClient(OpenAiVoiceProvider.ClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<IProtonVoiceProvider, OpenAiVoiceProvider>();
        services.AddSingleton<ProtonVoiceSessions>();
        services.AddSingleton<ProtonVoiceWatch>();
        services.AddHostedService(sp => sp.GetRequiredService<ProtonVoiceWatch>());
        services.AddScoped<ProtonVoiceService>();

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
        services.AddScoped<IProtonTool, GetMoneyByMonthTool>();
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

        // A Design project's files and client portal, and the discussion with its client, read as the person (Phase 2).
        services.AddSingleton<ProtonVideoFrames>();
        services.AddSingleton<ProtonFileReader>();
        services.AddScoped<IProtonTool, GetProjectFilesTool>();
        services.AddScoped<IProtonTool, ReadProjectFileTool>();
        services.AddScoped<IProtonTool, GetPortalStatusTool>();
        services.AddScoped<IProtonTool, ReadDiscussionTool>();

        // The company's public website: its projects, what it says, and its visitors.
        services.AddScoped<IProtonTool, GetWebsiteProjectsTool>();
        services.AddScoped<IProtonTool, GetWebsiteContentTool>();
        services.AddScoped<IProtonTool, GetWebsiteAnalyticsTool>();
        services.AddScoped<ProtonBriefingService>();

        // UI tools: they read nothing new, they ask the app to open a page or record the user may already open.
        services.AddScoped<ProtonRecords>();
        services.AddScoped<ProtonPortalTab>();
        services.AddScoped<IProtonTool, OpenRecordTool>();
        services.AddScoped<IProtonTool, OpenPageTool>();
        services.AddScoped<IProtonTool, OfferChoicesTool>();
        services.AddScoped<IProtonTool, SuggestRepliesTool>();
        services.AddScoped<IProtonTool, OpenSurfaceTool>();
        services.AddScoped<IProtonTool, FillFormTool>();
        services.AddScoped<IProtonTool, SetPreferenceTool>();
        services.AddScoped<IProtonTool, RefreshPageTool>();
        services.AddScoped<IProtonTool, PresentDataTool>();

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
        services.AddScoped<IProtonTool, ProposeCreateInvoiceTool>();
        services.AddScoped<IProtonTool, ProposeUpdateInvoiceTool>();
        services.AddScoped<IProtonTool, ProposeInvoiceStepTool>();
        services.AddScoped<IProtonTool, ProposeInvoiceDeliveryTool>();
        services.AddScoped<IProtonTool, ProposeRecordPaymentTool>();
        services.AddScoped<IProtonTool, ProposePaymentStepTool>();
        services.AddScoped<IProtonTool, ProposeAddExpenseTool>();
        services.AddScoped<IProtonTool, ProposeDeleteExpenseTool>();
        services.AddScoped<IProtonTool, ProposeAddMaterialsTool>();
        services.AddScoped<IProtonTool, ProposeMaterialStepTool>();
        services.AddScoped<IProtonTool, ProposeSubmitLabourWeekTool>();
        services.AddScoped<IProtonTool, ProposeLabourDefaultsTool>();
        services.AddScoped<IProtonTool, ProposeCreateStatementTool>();
        services.AddScoped<IProtonTool, ProposeStatementDeliveryTool>();
        services.AddScoped<IProtonTool, ProposeBillingSettingsTool>();
        services.AddScoped<IProtonTool, ProposeExchangeRateTool>();
        services.AddScoped<IProtonTool, ProposeProjectMoneyTool>();
        services.AddScoped<IProtonTool, ProposeOverheadTool>();
        services.AddScoped<IProtonTool, ProposeWorkingTimeTool>();
        services.AddScoped<IProtonTool, ProposeScheduleTool>();
        services.AddScoped<IProtonTool, ProposeHourExcuseTool>();
        services.AddScoped<IProtonTool, ProposeDeleteRecordTool>();

        // A Design project's files and its client portal (Phase 4): a file's title, bringing one back, a comment for
        // the team; publishing or discarding the portal is strong, in the Access family.
        services.AddScoped<IProtonTool, ProposeRenameFileTool>();
        services.AddScoped<IProtonTool, ProposeRestoreFileTool>();
        services.AddScoped<IProtonTool, ProposeCommentOnFileTool>();
        services.AddScoped<IProtonTool, ProposePortalStepTool>();

        // The company's public website: all strong, typed turns only, in the Website family (off by default).
        services.AddScoped<IProtonTool, ProposeWebsiteProjectTool>();
        services.AddScoped<IProtonTool, ProposeUnderConstructionTool>();
        services.AddScoped<IProtonTool, ProposeWebsiteServiceTool>();
        services.AddScoped<IProtonTool, ProposeWebsiteLocationTool>();
        services.AddScoped<IProtonTool, ProposeChangeProjectStatusTool>();
        services.AddScoped<IProtonTool, ProposeUpdateProjectTool>();
        services.AddScoped<IProtonTool, ProposeCreateProjectTool>();

        return services;
    }
}
