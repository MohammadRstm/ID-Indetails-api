using System.Globalization;
using ID_Indetails.LoadTests.Tool.Report;
using ID_Indetails.LoadTests.Tool.Seeding;
using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool;

/// <summary>Creates, seeds, checks, resets and drops the load-test database. See load-tests/README.md.</summary>
public static class Program
{
    private const string Usage = """
        Usage: dotnet run --project load-tests/tool -c Release -- <command>

          init                          Mark the server as this load-test environment (once) and create idi_loadtest
          check                         Verify the server, the migrations and email safety. Changes nothing
          migrate                       Apply the API's pending migrations to idi_loadtest
          reset --confirm idi_loadtest  Drop idi_loadtest, create it again and migrate it
          drop --confirm idi_loadtest   Drop idi_loadtest. Add --identity to remove the server's marker too
          seed                          Seed a freshly reset idi_loadtest through the API, then verify it. Options:
                                          --users 200  --months 3  --random-seed 42
                                          --anchor yyyy-MM-dd (default today; the history ends the day before)
                                          --roster load-tests/reports/roster.json (the test users, for k6)
          verify                        Check the rules the data must keep. Changes nothing. After a k6 run, also:
                                          --log <k6 console output>  --run <RUN_ID>  (what the run changed, the races)
                                          --out <folder>             (writes verification.json and verification.md)
          fingerprint                   Print a digest of every table, to compare two seeds. Changes nothing
          report --dir <run folder>     Write report.md and report.json from a run's results (needs no database)

        Settings come from the environment only:
          LT_DB_CONNECTION  admin connection string to the load-test server, with Database=idi_loadtest
          LT_DB_HOSTNAME    the hostname that server reports (SELECT @@hostname)
          LT_INSTANCE_ID    the load-test environment's id (LT_INSTANCE_ID in its .env)
          LT_USER_PASSWORD  seed only: the password every test user gets (at least 10 characters)
        """;

    private const string IdentityFlag = "--identity";

    private static readonly Dictionary<string, string[]> Options = new()
    {
        ["init"] = [], ["check"] = [], ["migrate"] = [], ["fingerprint"] = [],
        ["verify"] = ["--log", "--run", "--out"],
        ["report"] = ["--dir"],
        ["reset"] = ["--confirm"],
        ["drop"] = ["--confirm", IdentityFlag],
        ["seed"] = ["--users", "--months", "--random-seed", "--anchor", "--roster"],
    };

    public static async Task<int> Main(string[] args)
    {
        var command = Parse(args);
        var seed = command?.Name == "seed" ? SeedOptions(command.Options) : null;
        if (command is null || (command.Name == "seed" && seed is null))
        {
            Console.Error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        if (command.Name == "report")
        {
            if (command.Options.GetValueOrDefault("--dir") is not { } folder || !File.Exists(Path.Combine(folder, "k6-summary.json")))
            {
                Console.Error.WriteLine("report --dir needs a run folder holding k6-summary.json.");
                return ExitCodes.Usage;
            }

            Console.Write(await RunReport.WriteAsync(folder));
            return ExitCodes.Ok;
        }

        LoadTestSettings settings;
        try
        {
            settings = LoadTestSettings.Read(Environment.GetEnvironmentVariable);
        }
        catch (SettingsException ex)
        {
            foreach (var problem in ex.Problems)
            {
                Console.Error.WriteLine(problem);
            }

            return ExitCodes.Usage;
        }

        var commands = new Commands(settings, new LoadTestServer(settings), Console.Out);
        var confirmed = command.Options.GetValueOrDefault("--confirm") == LoadTestNames.Database;
        try
        {
            return command.Name switch
            {
                "init" => await commands.InitAsync(),
                "check" => await commands.CheckAsync(),
                "migrate" => await commands.MigrateAsync(),
                "reset" => await commands.ResetAsync(confirmed),
                "drop" => await commands.DropAsync(confirmed, command.Options.ContainsKey(IdentityFlag)),
                "seed" => await commands.SeedAsync(seed!.Value.Plan, seed.Value.Roster, Environment.GetEnvironmentVariable("LT_USER_PASSWORD")),
                "verify" => await commands.VerifyAsync(
                    command.Options.GetValueOrDefault("--log"), command.Options.GetValueOrDefault("--run"), command.Options.GetValueOrDefault("--out")),
                _ => await commands.FingerprintAsync(),
            };
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"MySQL error: {ex.Message}");
            return ExitCodes.Failed;
        }
        catch (SeedingException ex)
        {
            Console.Error.WriteLine($"SEED FAILED {ex.Message}");
            return ExitCodes.Failed;
        }
    }

    private sealed record Command(string Name, IReadOnlyDictionary<string, string> Options);

    private static Command? Parse(string[] args)
    {
        if (args.Length == 0 || !Options.TryGetValue(args[0], out var allowed))
        {
            return null;
        }

        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];
            if (!allowed.Contains(name) || options.ContainsKey(name))
            {
                return null;
            }

            if (name == IdentityFlag)
            {
                options[name] = "true";
            }
            else if (i + 1 < args.Length)
            {
                options[name] = args[++i];
            }
            else
            {
                return null;
            }
        }

        return new Command(args[0], options);
    }

    /// <summary>The seed plan from the options, or null when one of them isn't a number or a date.</summary>
    private static (SeedPlan Plan, string Roster)? SeedOptions(IReadOnlyDictionary<string, string> options)
    {
        static int? Number(string? text, int fallback) => text is null ? fallback : int.TryParse(text, out var n) ? n : null;

        var users = Number(options.GetValueOrDefault("--users"), 200);
        var months = Number(options.GetValueOrDefault("--months"), 3);
        var seed = Number(options.GetValueOrDefault("--random-seed"), 42);
        DateOnly? anchor = options.TryGetValue("--anchor", out var text)
            ? DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null
            : Commands.CompanyToday();
        if (users is null || months is null || seed is null || anchor is null)
        {
            return null;
        }

        return (new SeedPlan(users.Value, months.Value, seed.Value, anchor.Value),
            options.GetValueOrDefault("--roster") ?? Path.Combine("load-tests", "reports", "roster.json"));
    }
}
