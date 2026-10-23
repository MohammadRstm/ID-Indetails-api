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
    public string Model { get; init; } = "claude-haiku-5-5";

    /// <summary>low, medium, high, xhigh or max. Medium: money answers need the model to keep agreed and received figures apart.</summary>
    [RegularExpression("^(low|medium|high|xhigh|max)$")]
    public string Effort { get; init; } = "medium";

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

    /// <summary>
    /// What each text question holds of the budget while it is answered, in USD: an estimate above a typical question's
    /// cost, so questions asked at once can't together spend past the budget. Let go once the real cost is recorded.
    /// </summary>
    [Range(0, 100)]
    public decimal QuestionHoldUsd { get; init; } = 0.25m;

    /// <summary>Questions each person may ask a month: the allowance people see instead of costs.</summary>
    [Range(1, 100_000)]
    public int MonthlyQuestionsPerUser { get; init; } = 300;

    /// <summary>
    /// Past this many tools for a person, all but the core set are deferred and found through tool search, so the
    /// model keeps choosing well as the list grows.
    /// </summary>
    [Range(10, 500)]
    public int DeferToolsAbove { get; init; } = 55;

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

    public ProtonVoiceOptions Voice { get; init; } = new();

    public ProtonHistoryOptions History { get; init; } = new();

    public ProtonVisualOptions Visuals { get; init; } = new();
}

/// <summary>
/// Charts, tables and headline figures in answers (present_data). Off, the tool stays listed (so the prompt cache
/// holds) but refuses, and answers are words only.
/// </summary>
public sealed class ProtonVisualOptions
{
    public bool Enabled { get; init; } = true;

    [Range(1, 10)]
    public int MaxBlocksPerAnswer { get; init; } = 3;

    /// <summary>How old data may be and still be shown: older, it is read again first.</summary>
    [Range(1, 240)]
    public int DatasetMinutes { get; init; } = 15;
}

/// <summary>Saved chats: how long one nobody uses is kept before the daily job deletes it.</summary>
public sealed class ProtonHistoryOptions
{
    [Range(1, 3650)]
    public int RetentionDays { get; init; } = 90;

    /// <summary>Off in tests, so the job doesn't run behind them.</summary>
    public bool CleanupEnabled { get; init; } = true;
}

/// <summary>
/// Talking to Proton through OpenAI's GPT-Live: the live model listens and speaks, and hands every question to the
/// same Proton agent as the text chat. Off unless <see cref="Enabled"/> is set and a key is configured; the key comes
/// from user secrets locally and the <c>Proton__Voice__ApiKey</c> environment variable in production.
/// </summary>
public sealed class ProtonVoiceOptions
{
    public bool Enabled { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Model { get; init; } = "gpt-live-1";

    /// <summary>One of the live model's voices; it can't change during a session.</summary>
    [Required]
    public string Voice { get; init; } = "marin";

    /// <summary>The provider's API, sessions are created under it and the server's watch attaches to it.</summary>
    [Required]
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";

    /// <summary>The provider's price per minute of voice, billed per second, to estimate cost.</summary>
    [Range(0, 100)]
    public decimal PricePerMinuteUsd { get; init; } = 0.05m;

    /// <summary>A session is closed by the server after this long, whatever the browser does.</summary>
    [Range(1, 60)]
    public int MaxSessionMinutes { get; init; } = 10;

    /// <summary>How long a closing session may take to report its final usage before the server gives up on it.</summary>
    [Range(1, 120)]
    public int CloseWaitSeconds { get; init; } = 15;

    public bool IsOn => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
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

/// <summary>
/// USD per million tokens of <see cref="ProtonOptions.Model"/>, used to estimate cost. The defaults are Claude Haiku
/// 5.5's list prices: cache reads at 0.1x the input price, five-minute cache writes at 1.25x, and a second rate card
/// for a model call whose prompt is longer than 100,000 tokens. Change them with the model.
/// </summary>
public sealed class ProtonPrices
{
    [Range(0, 1_000)]
    public decimal InputPerMillion { get; init; } = 0.10m;

    [Range(0, 1_000)]
    public decimal OutputPerMillion { get; init; } = 0.50m;

    [Range(0, 1_000)]
    public decimal CacheReadPerMillion { get; init; } = 0.01m;

    [Range(0, 1_000)]
    public decimal CacheWritePerMillion { get; init; } = 0.125m;

    /// <summary>A call whose prompt (cached or not) is longer than this many tokens is priced at the Long… rates; 0 for one rate card.</summary>
    [Range(0, 10_000_000)]
    public int LongPromptAboveTokens { get; init; } = 100_000;

    [Range(0, 1_000)]
    public decimal LongInputPerMillion { get; init; } = 0.50m;

    [Range(0, 1_000)]
    public decimal LongOutputPerMillion { get; init; } = 2.50m;

    [Range(0, 1_000)]
    public decimal LongCacheReadPerMillion { get; init; } = 0.05m;

    [Range(0, 1_000)]
    public decimal LongCacheWritePerMillion { get; init; } = 0.625m;
}
