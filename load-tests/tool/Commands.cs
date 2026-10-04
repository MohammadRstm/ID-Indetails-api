using ID_Indetails.LoadTests.Tool.Safety;

namespace ID_Indetails.LoadTests.Tool;

public static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>Refused by a guard, or the database isn't ready for a test.</summary>
    public const int Failed = 1;

    public const int Usage = 2;
}

/// <summary>
/// The tool's commands. Each one reads what the server says about itself and runs <see cref="TargetGuard"/> before
/// it changes anything. The ones that delete data also want the database's name typed out.
/// </summary>
public sealed class Commands(LoadTestSettings settings, LoadTestServer server, TextWriter output)
{
    private const string Database = LoadTestNames.Database;

    public async Task<int> InitAsync()
    {
        if (await GuardedIdentityAsync(GuardMode.Initialise) is not { } identity)
        {
            return ExitCodes.Failed;
        }

        if (identity.Marker is null)
        {
            await server.CreateIdentityAsync(settings.InstanceId);
            output.WriteLine($"Marked     the server as load-test instance {TargetGuard.Short(settings.InstanceId)}");
        }

        await server.CreateDatabaseAsync();
        output.WriteLine($"Database   {Database} exists");
        return ExitCodes.Ok;
    }

    public async Task<int> CheckAsync()
        => await GuardedIdentityAsync(GuardMode.Existing) is { } identity && await ReportReadinessAsync(identity)
            ? ExitCodes.Ok
            : ExitCodes.Failed;

    public async Task<int> MigrateAsync()
    {
        if (await GuardedIdentityAsync(GuardMode.Existing) is not { } identity)
        {
            return ExitCodes.Failed;
        }

        if (!identity.DatabaseExists)
        {
            output.WriteLine($"REFUSED    {Database} doesn't exist. Run init or reset.");
            return ExitCodes.Failed;
        }

        await server.MigrateAsync();
        return await ReportReadinessAsync(await server.ReadIdentityAsync()) ? ExitCodes.Ok : ExitCodes.Failed;
    }

    public async Task<int> ResetAsync(bool confirmed)
    {
        if (!confirmed)
        {
            return AskForConfirmation("reset");
        }

        if (await GuardedIdentityAsync(GuardMode.Existing) is null)
        {
            return ExitCodes.Failed;
        }

        await server.DropDatabaseAsync();
        await server.CreateDatabaseAsync();
        await server.MigrateAsync();
        output.WriteLine($"Reset      dropped {Database}, created it again and migrated it");
        return await ReportReadinessAsync(await server.ReadIdentityAsync()) ? ExitCodes.Ok : ExitCodes.Failed;
    }

    public async Task<int> DropAsync(bool confirmed, bool withIdentity)
    {
        if (!confirmed)
        {
            return AskForConfirmation("drop");
        }

        if (await GuardedIdentityAsync(GuardMode.Existing) is null)
        {
            return ExitCodes.Failed;
        }

        await server.DropDatabaseAsync();
        output.WriteLine($"Dropped    {Database}");
        if (withIdentity)
        {
            await server.DropIdentityAsync();
            output.WriteLine($"Dropped    {LoadTestNames.IdentityDatabase}: the server is no longer marked as a load-test server");
        }

        return ExitCodes.Ok;
    }

    /// <summary>The server's identity when the guard lets the command go ahead, otherwise null.</summary>
    private async Task<ServerIdentity?> GuardedIdentityAsync(GuardMode mode)
    {
        var identity = await server.ReadIdentityAsync();
        output.WriteLine($"Server     {settings.Describe()}, hostname {identity.Hostname}, MySQL {identity.Version}");
        output.WriteLine(identity.Marker is { } marker
            ? $"Identity   {marker.Environment} instance {TargetGuard.Short(marker.InstanceId)}"
            : "Identity   none");

        var problems = TargetGuard.Problems(settings, identity, mode);
        foreach (var problem in problems)
        {
            output.WriteLine($"REFUSED    {problem}");
        }

        return problems.Count == 0 ? identity : null;
    }

    /// <summary>Whether the database is ready for a test: migrated by this API version and holding no deliverable address.</summary>
    private async Task<bool> ReportReadinessAsync(ServerIdentity identity)
    {
        if (!identity.DatabaseExists)
        {
            output.WriteLine($"NOT READY  {Database} doesn't exist. Run init.");
            return false;
        }

        var migrations = await server.ReadMigrationStatusAsync();
        output.WriteLine($"Database   {Database}: {migrations.Applied}/{migrations.Known} migrations, {identity.TableCount} tables");
        if (migrations.Unknown.Count > 0)
        {
            output.WriteLine($"NOT READY  It has migrations this tool doesn't know, from a newer API: {string.Join(", ", migrations.Unknown)}");
            return false;
        }

        if (!migrations.UpToDate)
        {
            output.WriteLine("NOT READY  Not fully migrated. Run migrate or reset.");
            return false;
        }

        var deliverable = await server.FindDeliverableAddressesAsync();
        foreach (var (column, rows) in deliverable)
        {
            output.WriteLine($"NOT READY  {rows} rows of {column} hold an address outside @{EmailSafety.Domain}");
        }

        if (deliverable.Count == 0)
        {
            output.WriteLine($"Email      every address ends in @{EmailSafety.Domain}");
        }

        return deliverable.Count == 0;
    }

    private int AskForConfirmation(string command)
    {
        output.WriteLine($"This deletes the load-test data. Run it as: {command} --confirm {Database}");
        return ExitCodes.Usage;
    }
}
