namespace ID_Indetails.LoadTests.Tool.Safety;

/// <summary>What a database server says about itself before the tool changes anything on it.</summary>
public sealed record ServerIdentity(string Hostname, string Version, IdentityMarker? Marker, bool DatabaseExists, int TableCount);

/// <summary>The row in <see cref="LoadTestNames.IdentityDatabase"/> that marks a server as a load-test server.</summary>
public sealed record IdentityMarker(string Environment, Guid InstanceId);

public enum GuardMode
{
    /// <summary>Marking a server for the first time: it may have no marker yet, but must hold no unmarked data.</summary>
    Initialise,

    /// <summary>Every other command: the server must already carry this environment's marker.</summary>
    Existing,
}

/// <summary>
/// Decides whether the tool may work on a server. Every check must pass. Together with the fixed database name
/// (<see cref="LoadTestNames.Database"/>) they mean the tool can only ever change one database, on a server that was
/// deliberately marked as this load-test environment.
/// </summary>
public static class TargetGuard
{
    public static IReadOnlyList<string> Problems(LoadTestSettings settings, ServerIdentity server, GuardMode mode)
    {
        var problems = new List<string>();

        if (!string.Equals(server.Hostname, settings.ExpectedHostname, StringComparison.Ordinal))
        {
            problems.Add($"The server says its hostname is '{server.Hostname}', but {LoadTestSettings.HostnameVariable} is " +
                $"'{settings.ExpectedHostname}'. This is not the load-test database server.");
        }

        switch (server.Marker)
        {
            case null when mode == GuardMode.Existing:
                problems.Add($"The server has no load-test identity ({LoadTestNames.IdentityDatabase}). " +
                    "Run init first, and only against the load-test server.");
                break;
            case null when server.TableCount > 0:
                problems.Add($"'{LoadTestNames.Database}' already holds {server.TableCount} tables, but the server has no " +
                    "load-test identity. The tool won't mark or touch a database it didn't create.");
                break;
            case { } marker when marker.Environment != LoadTestNames.Environment:
                problems.Add($"The server's identity says '{marker.Environment}', not '{LoadTestNames.Environment}'.");
                break;
            case { } marker when marker.InstanceId != settings.InstanceId:
                problems.Add($"The server belongs to load-test instance {Short(marker.InstanceId)}, but " +
                    $"{LoadTestSettings.InstanceVariable} is {Short(settings.InstanceId)}.");
                break;
        }

        return problems;
    }

    public static string Short(Guid id) => id.ToString("N")[..8];
}
