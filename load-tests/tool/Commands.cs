using ID_Indetails.LoadTests.Tool.Safety;
using ID_Indetails.LoadTests.Tool.Seeding;
using ID_Indetails.LoadTests.Tool.Verification;

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

    /// <summary>
    /// Seeds a freshly reset database through the API, then checks the result: what was created against what the
    /// database holds, the data rules and email safety. Writes the roster of test users for k6.
    /// </summary>
    public async Task<int> SeedAsync(SeedPlan plan, string rosterPath, string? password)
    {
        if (plan.Users < SeedPlan.MinimumUsers || plan.Months is < 1 or > 12 || plan.Anchor > CompanyToday())
        {
            output.WriteLine($"REFUSED    Seed at least {SeedPlan.MinimumUsers} users over 1 to 12 months, anchored today or earlier.");
            return ExitCodes.Usage;
        }

        if (password is not { Length: >= 10 })
        {
            output.WriteLine("REFUSED    Set LT_USER_PASSWORD, at least 10 characters: every test user signs in with it.");
            return ExitCodes.Usage;
        }

        if (await GuardedIdentityAsync(GuardMode.Existing) is not { } identity || !await ReportReadinessAsync(identity))
        {
            return ExitCodes.Failed;
        }

        if (!await server.IsEmptyAsync())
        {
            output.WriteLine($"REFUSED    {Database} already holds people. Seed a fresh one: reset --confirm {Database} first.");
            return ExitCodes.Failed;
        }

        var world = await new Seeder(settings, plan, password, output).RunAsync();
        await Seeder.WriteRosterAsync(world, rosterPath);
        output.WriteLine($"Roster     {Path.GetFullPath(rosterPath)}");

        var counts = await SeedCounts.CompareAsync(settings.ConnectionString, world.Tally.Counts);
        foreach (var count in counts)
        {
            output.WriteLine($"{(count.Matches ? "Count" : "MISMATCH"),-10} {count.What}: created {count.Expected}, database {count.Actual}");
        }

        var rulesHold = await ReportChecksAsync();
        var ready = await ReportReadinessAsync(await server.ReadIdentityAsync());
        return counts.All(c => c.Matches) && rulesHold && ready ? ExitCodes.Ok : ExitCodes.Failed;
    }

    /// <summary>
    /// The data rules, and with a k6 run's console output, what that run changed against what the database holds and
    /// the race checks. With an output folder, writes verification.json and verification.md there.
    /// </summary>
    public async Task<int> VerifyAsync(string? runLogPath, string? runId, string? outFolder)
    {
        if (await GuardedIdentityAsync(GuardMode.Existing) is not { } identity || !await ReportReadinessAsync(identity))
        {
            return ExitCodes.Failed;
        }

        var rules = await DataChecks.RunAsync(settings.ConnectionString);
        var log = runLogPath is null ? new RunLog() : RunLog.Read(runLogPath);
        IReadOnlyList<Expectation> ledger = runId is null || log.Ledger.Count == 0
            ? []
            : await LedgerChecks.CompareAsync(settings.ConnectionString, runId, log.Ledger);
        var races = log.Afterwards
            .Select(a => (a.GetProperty("race").GetString()!, a.GetProperty("rule").GetString()!, a.GetProperty("holds").GetBoolean()))
            .ToList();
        var changes = log.Ledger.GroupBy(e => e.GetProperty("kind").GetString()!).ToDictionary(g => g.Key, g => g.Count());

        var report = new VerificationReport(runId ?? "none", rules, ledger, races, changes);
        output.Write(report.Markdown());
        if (outFolder is not null)
        {
            await report.WriteAsync(outFolder);
        }

        return report.Passed ? ExitCodes.Ok : ExitCodes.Failed;
    }

    public async Task<int> FingerprintAsync()
    {
        if (await GuardedIdentityAsync(GuardMode.Existing) is null)
        {
            return ExitCodes.Failed;
        }

        foreach (var line in await Fingerprint.TakeAsync(settings.ConnectionString))
        {
            output.WriteLine(line);
        }

        return ExitCodes.Ok;
    }

    /// <summary>Today in the company's time zone, by the real clock.</summary>
    public static DateOnly CompanyToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(SimulatedClock.TimeZone)));

    private async Task<bool> ReportChecksAsync()
    {
        var results = await DataChecks.RunAsync(settings.ConnectionString);
        foreach (var result in results)
        {
            output.WriteLine(result.Violations == 0 ? $"Holds      {result.Name}" : $"BROKEN     {result.Name}: {result.Violations} rows");
        }

        return results.All(r => r.Violations == 0);
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
