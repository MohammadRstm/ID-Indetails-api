using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// Proton, the read-only company assistant. Off unless <see cref="Enabled"/> is set and an API key is configured.
/// The key comes from user secrets locally and the <c>Proton__ApiKey</c> environment variable in production.
/// </summary>
public sealed class ProtonOptions
{
    public const string SectionName = "Proton";

    public bool Enabled { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Model { get; init; } = "claude-sonnet-5-5";

    /// <summary>low, medium, high, xhigh or max. Low keeps chat answers fast and cheap.</summary>
    [RegularExpression("^(low|medium|high|xhigh|max)$")]
    public string Effort { get; init; } = "low";

    [Range(1_000, 64_000)]
    public int MaxOutputTokens { get; init; } = 8_000;

    /// <summary>Model calls per question: each tool round trip is one.</summary>
    [Range(1, 20)]
    public int MaxRoundTrips { get; init; } = 8;

    [Range(1, 40)]
    public int MaxToolCallsPerQuestion { get; init; } = 12;

    [Range(5, 300)]
    public int ModelTimeoutSeconds { get; init; } = 40;

    [Range(1, 120)]
    public int ToolTimeoutSeconds { get; init; } = 20;

    /// <summary>The whole question, every model call and tool included. Stays under nginx's 60-second read timeout on /api.</summary>
    [Range(10, 600)]
    public int QuestionTimeoutSeconds { get; init; } = 55;

    /// <summary>
    /// The whole question on the streaming endpoint. It may run longer: its pings keep the connection busy, so no
    /// proxy's read timeout is reached while Proton works.
    /// </summary>
    [Range(10, 600)]
    public int StreamQuestionTimeoutSeconds { get; init; } = 120;

    [Range(1, 100)]
    public int MaxQuestionsPerConversation { get; init; } = 20;

    /// <summary>How long a proposed action can be confirmed: enough to read the card, short enough that it doesn't apply to a project that has moved on.</summary>
    [Range(1, 60)]
    public int ActionMinutes { get; init; } = 10;

    [Range(5, 240)]
    public int ConversationIdleMinutes { get; init; } = 30;

    /// <summary>The company's monthly budget in USD before top-ups. Only super admins see money figures.</summary>
    [Range(0, 1_000_000)]
    public decimal MonthlyBudgetUsd { get; init; } = 50m;

    /// <summary>Questions each person may ask a month: the allowance people see instead of costs.</summary>
    [Range(1, 100_000)]
    public int MonthlyQuestionsPerUser { get; init; } = 300;

    /// <summary>Analytical tools that load every project's money or all of a period's work: at most this many per question.</summary>
    [Range(1, 20)]
    public int MaxHeavyToolCallsPerQuestion { get; init; } = 4;

    /// <summary>
    /// A conversation whose last request sent more input tokens than this (cached or not) rolls over to a recap:
    /// every question re-sends the earlier ones with their tool results, so long analytical threads get expensive.
    /// </summary>
    [Range(10_000, 1_000_000)]
    public int MaxConversationInputTokens { get; init; } = 80_000;

    public ProtonPrices Prices { get; init; } = new();

    public ProtonInsightOptions Insights { get; init; } = new();

    public ProtonActionOptions Actions { get; init; } = new();
}

/// <summary>
/// Which kinds of change Proton may propose, and how many (docs/proton.md, "The action engine"). Everything outside Core is off
/// until it is switched on here; a switched-off kind keeps its tools (so the prompt cache holds) but refuses them.
/// </summary>
public sealed class ProtonActionOptions
{
    /// <summary>All of Proton's actions, Core included: off means it proposes nothing.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Completing, archiving and reopening projects.</summary>
    public bool Lifecycle { get; init; }

    public bool Deletes { get; init; }

    /// <summary>People's details, deactivating and reactivating, sessions and sign-in.</summary>
    public bool Access { get; init; }

    public bool CompanyRules { get; init; }

    /// <summary>Working time, hour excuses and schedules.</summary>
    public bool PayRules { get; init; }

    public bool Money { get; init; }

    /// <summary>Sending messages as the person.</summary>
    public bool Messaging { get; init; }

    /// <summary>Proposals waiting for a Confirm at once, per conversation.</summary>
    [Range(1, 10)]
    public int MaxPendingActions { get; init; } = 3;

    [Range(1, 1_000)]
    public int MaxConfirmedActionsPerHour { get; init; } = 30;

    [Range(1, 500)]
    public int MaxMessagesPerDay { get; init; } = 20;

    public bool IsOn(Proton.Actions.ProtonFamily family) => Enabled && family switch
    {
        Proton.Actions.ProtonFamily.Core => true,
        Proton.Actions.ProtonFamily.Lifecycle => Lifecycle,
        Proton.Actions.ProtonFamily.Deletes => Deletes,
        Proton.Actions.ProtonFamily.Access => Access,
        Proton.Actions.ProtonFamily.CompanyRules => CompanyRules,
        Proton.Actions.ProtonFamily.PayRules => PayRules,
        Proton.Actions.ProtonFamily.Money => Money,
        Proton.Actions.ProtonFamily.Messaging => Messaging,
        _ => false,
    };
}

/// <summary>
/// When a measured change may be called notable or unusual (docs/proton.md, "Analysis"). Without one of these rules
/// firing, Proton reports the change and makes no judgement about its size.
/// </summary>
public sealed class ProtonInsightOptions
{
    [Range(1, 1_000)]
    public int NotablePct { get; init; } = 30;

    [Range(0, 10_000)]
    public decimal NotableHours { get; init; } = 10m;

    [Range(0, 10_000_000)]
    public decimal NotableUsd { get; init; } = 1_000m;

    [Range(1, 100)]
    public decimal UnusualHigh { get; init; } = 1.5m;

    [Range(0, 1)]
    public decimal UnusualLow { get; init; } = 0.5m;

    [Range(2, 12)]
    public int BaselinePeriods { get; init; } = 4;

    [Range(1, 365)]
    public int TargetSoonDays { get; init; } = 14;
}

/// <summary>USD per million tokens of <see cref="ProtonOptions.Model"/>, used to estimate cost.</summary>
public sealed class ProtonPrices
{
    [Range(0, 1_000)]
    public decimal InputPerMillion { get; init; } = 2m;

    [Range(0, 1_000)]
    public decimal OutputPerMillion { get; init; } = 10m;

    [Range(0, 1_000)]
    public decimal CacheReadPerMillion { get; init; } = 0.20m;

    [Range(0, 1_000)]
    public decimal CacheWritePerMillion { get; init; } = 2.50m;
}
