using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.ContentStore.Abstraction;
using ID_Indetails.api.Services.ContentStore.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Extensions;

public static class ContentStoreServiceCollectionExtensions
{
    /// <summary>
    /// The settings of the client portal's file storage (docs/content-storage.md), checked at start-up, and the store they
    /// choose. Call it before the services that Proton guards are wrapped.
    /// </summary>
    /// <summary>
    /// Where the folder-on-disk store may run: its links are served anonymously and without size or rate limits, so
    /// anywhere else (production, a staging server) must use R2.
    /// </summary>
    private static readonly string[] LocalEnvironments = [Environments.Development, "Testing", "Seeding"];

    public static IServiceCollection AddContentStore(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<ContentStoreOptions>().BindConfiguration(ContentStoreOptions.SectionName)
            .RequireForR2("AccountId", r2 => r2.AccountId)
            .RequireForR2("Bucket", r2 => r2.Bucket)
            .RequireForR2("AccessKeyId", r2 => r2.AccessKeyId)
            .RequireForR2("SecretAccessKey", r2 => r2.SecretAccessKey)
            .Validate(o => o.Provider != ContentStoreProvider.R2 || string.IsNullOrWhiteSpace(o.R2.Endpoint) || IsHttps(o.R2.Endpoint),
                "ContentStore:R2:Endpoint must be an https URL, or empty to use https://{AccountId}.r2.cloudflarestorage.com.")
            .Validate(o => o.Provider != ContentStoreProvider.Local || LocalEnvironments.Contains(environment.EnvironmentName),
                "ContentStore:Provider can be Local only in Development, Testing and Seeding: a folder on disk is for development and tests only. Use R2 (docs/content-storage.md).")
            .Validate(o => o.Provider != ContentStoreProvider.Local || !string.IsNullOrWhiteSpace(o.Local.RootPath), "ContentStore:Local:RootPath can't be empty.")
            .ValidateOnStart();

        var provider = configuration.GetSection(ContentStoreOptions.SectionName).Get<ContentStoreOptions>()?.Provider ?? ContentStoreProvider.None;
        switch (provider)
        {
            case ContentStoreProvider.R2:
                services.AddSingleton<IContentStore, R2ContentStore>();
                break;
            case ContentStoreProvider.Local:
                // also by its own type, for the controller that serves the links; absent in the other modes, where it answers 404
                services.AddSingleton<LocalContentStore>();
                services.AddSingleton<IContentStore>(sp => sp.GetRequiredService<LocalContentStore>());
                break;
            default:
                services.AddSingleton<IContentStore, UnconfiguredContentStore>();
                break;
        }

        return services;
    }

    private static OptionsBuilder<ContentStoreOptions> RequireForR2(
        this OptionsBuilder<ContentStoreOptions> builder, string name, Func<R2ContentStoreOptions, string> value)
        => builder.Validate(
            o => o.Provider != ContentStoreProvider.R2 || !string.IsNullOrWhiteSpace(value(o.R2)),
            $"ContentStore:R2:{name} is required when ContentStore:Provider is R2.");

    private static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
