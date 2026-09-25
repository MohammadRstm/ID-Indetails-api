using ID_Indetails.api.Data;
using ID_Indetails.api.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ID_Indetails.UnitTests.Data;

/// <summary>
/// Builds the real EF Core model (MySQL provider, snake_case naming) without opening a
/// connection, so schema rules can be checked in fast unit tests.
/// </summary>
internal static class ModelTestContext
{
    private static readonly Lazy<IModel> DesignTimeModel = new(() =>
    {
        var options = new DbContextOptionsBuilder<IdIndetailsDbContext>()
            .UseMySql("Server=localhost;Database=model_tests", DatabaseServiceCollectionExtensions.MySqlServerVersion)
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new IdIndetailsDbContext(options);

        // The design-time model keeps check constraints and seed data, which the runtime model drops.
        return context.GetService<IDesignTimeModel>().Model;
    });

    public static IModel Model => DesignTimeModel.Value;
}
