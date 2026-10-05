using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool;

/// <summary>
/// Where the load-test database is. Read from the environment only: the tool never reads the API's settings, user
/// secrets or <c>ConnectionStrings:Default</c>, so it can't pick up a database it wasn't explicitly given.
/// </summary>
public sealed record LoadTestSettings(string ConnectionString, string ExpectedHostname, Guid InstanceId)
{
    public const string ConnectionVariable = "LT_DB_CONNECTION";
    public const string HostnameVariable = "LT_DB_HOSTNAME";
    public const string InstanceVariable = "LT_INSTANCE_ID";

    /// <summary>The settings, or a <see cref="SettingsException"/> listing everything wrong with them.</summary>
    public static LoadTestSettings Read(Func<string, string?> variable)
    {
        var problems = new List<string>();

        var connection = variable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            problems.Add($"{ConnectionVariable} is not set: an admin connection string to the load-test server, with Database={LoadTestNames.Database}.");
        }
        else if (DatabaseOf(connection) is var database && database != LoadTestNames.Database)
        {
            problems.Add(database is null
                ? $"{ConnectionVariable} is not a valid MySQL connection string."
                : $"{ConnectionVariable} names the database '{database}'. The tool only works on '{LoadTestNames.Database}'.");
        }

        var hostname = variable(HostnameVariable);
        if (string.IsNullOrWhiteSpace(hostname))
        {
            problems.Add($"{HostnameVariable} is not set: the hostname the load-test database server reports (SELECT @@hostname).");
        }

        if (!Guid.TryParse(variable(InstanceVariable), out var instanceId) || instanceId == Guid.Empty)
        {
            problems.Add($"{InstanceVariable} is not a GUID: the id of this load-test environment, as in its .env.");
        }

        if (problems.Count > 0)
        {
            throw new SettingsException(problems);
        }

        return new LoadTestSettings(connection!, hostname!.Trim(), instanceId);
    }

    /// <summary>The same server with no database selected, for creating and dropping databases.</summary>
    public string ServerConnectionString
        => new MySqlConnectionStringBuilder(ConnectionString) { Database = string.Empty }.ConnectionString;

    /// <summary>Server, port and user, never the password.</summary>
    public string Describe()
    {
        var builder = new MySqlConnectionStringBuilder(ConnectionString);
        return $"{builder.Server}:{builder.Port} as {builder.UserID}";
    }

    private static string? DatabaseOf(string connectionString)
    {
        try
        {
            return new MySqlConnectionStringBuilder(connectionString).Database;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public sealed class SettingsException(IReadOnlyList<string> problems)
    : Exception(string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
