namespace ID_Indetails.IntegrationTests.Data;

/// <summary>
/// Where the integration tests find a MySQL server they may create throwaway databases on.
/// </summary>
internal static class TestDatabaseSettings
{
    /// <summary>Full admin connection string without a database, e.g. for CI.</summary>
    public const string AdminConnectionVariable = "ID_INDETAILS_TEST_MYSQL_ADMIN";

    /// <summary>
    /// Uses <see cref="AdminConnectionVariable"/> when set. Otherwise builds a root connection
    /// to the local Docker database from the repository's gitignored <c>.env</c> file.
    /// </summary>
    public static string AdminConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(AdminConnectionVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var env = ReadDotEnv()
            ?? throw new InvalidOperationException(
                $"No test database configured. Set {AdminConnectionVariable}, or create .env and start " +
                "the Docker database (see docs/local-development.md).");

        var port = env.GetValueOrDefault("MYSQL_HOST_PORT", "3308");
        var rootPassword = env.GetValueOrDefault("MYSQL_ROOT_PASSWORD")
            ?? throw new InvalidOperationException("MYSQL_ROOT_PASSWORD is missing from .env.");

        return $"Server=127.0.0.1;Port={port};User=root;Password={rootPassword};";
    }

    private static Dictionary<string, string>? ReadDotEnv()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path) || !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                continue;
            }

            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
                .Select(line => line.Split('=', 2))
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());
        }

        return null;
    }
}
