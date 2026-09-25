using ID_Indetails.api.Data;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Extensions;

public static class DatabaseServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    /// <summary>MySQL version the Docker Compose database runs (see docker-compose.yml).</summary>
    public static readonly ServerVersion MySqlServerVersion = new MySqlServerVersion(new Version(8, 4, 0));

    /// <summary>
    /// Registers the EF Core context against MySQL. The connection string is read from
    /// <c>ConnectionStrings:Default</c>, which must come from user secrets or environment
    /// variables, never from a committed appsettings file.
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                "Set it with 'dotnet user-secrets' (see docs/local-development.md).");
        }

        services.AddDbContext<IdIndetailsDbContext>(options => options
            .UseMySql(connectionString, MySqlServerVersion)
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
