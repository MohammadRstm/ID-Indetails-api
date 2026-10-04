using MySqlConnector;

namespace ID_Indetails.LoadTests.Tool;

/// <summary>Creates, checks, resets and drops the load-test database. See load-tests/README.md.</summary>
public static class Program
{
    private const string Usage = """
        Usage: dotnet run --project load-tests/tool -c Release -- <command>

          init                          Mark the server as this load-test environment (once) and create idi_loadtest
          check                         Verify the server, the migrations and email safety. Changes nothing
          migrate                       Apply the API's pending migrations to idi_loadtest
          reset --confirm idi_loadtest  Drop idi_loadtest, create it again and migrate it
          drop --confirm idi_loadtest   Drop idi_loadtest. Add --identity to remove the server's marker too

        Settings come from the environment only:
          LT_DB_CONNECTION  admin connection string to the load-test server, with Database=idi_loadtest
          LT_DB_HOSTNAME    the hostname that server reports (SELECT @@hostname)
          LT_INSTANCE_ID    the load-test environment's id (LT_INSTANCE_ID in its .env)
        """;

    public static async Task<int> Main(string[] args)
    {
        if (Parse(args) is not { } command)
        {
            Console.Error.WriteLine(Usage);
            return ExitCodes.Usage;
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
        try
        {
            return command.Name switch
            {
                "init" => await commands.InitAsync(),
                "check" => await commands.CheckAsync(),
                "migrate" => await commands.MigrateAsync(),
                "reset" => await commands.ResetAsync(command.Confirmed),
                _ => await commands.DropAsync(command.Confirmed, command.WithIdentity),
            };
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"MySQL error: {ex.Message}");
            return ExitCodes.Failed;
        }
    }

    private sealed record Command(string Name, bool Confirmed, bool WithIdentity);

    private static Command? Parse(string[] args)
    {
        if (args is not [("init" or "check" or "migrate" or "reset" or "drop") and var name, .. var options])
        {
            return null;
        }

        var confirmed = false;
        var withIdentity = false;
        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--confirm" when name is "reset" or "drop" && i + 1 < options.Length:
                    confirmed = options[++i] == LoadTestNames.Database;
                    break;
                case "--identity" when name is "drop":
                    withIdentity = true;
                    break;
                default:
                    return null;
            }
        }

        return new Command(name, confirmed, withIdentity);
    }
}
