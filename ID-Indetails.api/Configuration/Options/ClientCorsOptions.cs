namespace ID_Indetails.api.Configuration.Options;

/// <summary>Origins of the React client allowed to call the API with credentials (the refresh cookie).</summary>
public sealed class ClientCorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "Client";

    public string[] AllowedOrigins { get; init; } = [];
}
