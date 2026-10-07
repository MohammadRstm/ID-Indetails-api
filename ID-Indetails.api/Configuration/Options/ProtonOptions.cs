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

    public ProtonPrices Prices { get; init; } = new();
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
