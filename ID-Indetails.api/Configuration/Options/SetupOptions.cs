namespace ID_Indetails.api.Configuration.Options;

/// <summary>
/// Developer-only setup. <see cref="SuperAdminKey"/> is a secret: user secrets or environment
/// only. When it is empty the setup endpoint behaves as if it does not exist.
/// </summary>
public sealed class SetupOptions
{
    public const string SectionName = "Setup";

    public const string SuperAdminKeyHeader = "X-Setup-Key";

    public string SuperAdminKey { get; init; } = string.Empty;
}
