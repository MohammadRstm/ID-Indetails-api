using Shouldly;

namespace ID_Indetails.LoadTests.Tool.Tests;

public class LoadTestSettingsTests
{
    private const string Connection = "Server=127.0.0.1;Port=13306;User=root;Password=secret-pw;Database=idi_loadtest;";
    private static readonly string Instance = Guid.NewGuid().ToString();

    private static LoadTestSettings Read(string? connection = Connection, string? hostname = "idi-loadtest-db", string? instance = null)
    {
        var values = new Dictionary<string, string?>
        {
            [LoadTestSettings.ConnectionVariable] = connection,
            [LoadTestSettings.HostnameVariable] = hostname,
            [LoadTestSettings.InstanceVariable] = instance ?? Instance,
        };
        return LoadTestSettings.Read(name => values.GetValueOrDefault(name));
    }

    [Fact]
    public void Reads_complete_settings()
    {
        var settings = Read();

        settings.ExpectedHostname.ShouldBe("idi-loadtest-db");
        settings.InstanceId.ShouldBe(Guid.Parse(Instance));
    }

    [Theory]
    [InlineData("Server=127.0.0.1;User=root;Password=x;Database=id_indetails;")]
    [InlineData("Server=127.0.0.1;User=root;Password=x;Database=IDI_LOADTEST;")]
    [InlineData("Server=127.0.0.1;User=root;Password=x;Database=idi_loadtest2;")]
    [InlineData("Server=127.0.0.1;User=root;Password=x;")]
    public void Refuses_any_database_but_the_load_test_one(string connection)
    {
        var ex = Should.Throw<SettingsException>(() => Read(connection: connection));

        ex.Problems.ShouldHaveSingleItem().ShouldContain("only works on 'idi_loadtest'");
    }

    [Fact]
    public void Refuses_a_malformed_connection_string()
    {
        Should.Throw<SettingsException>(() => Read(connection: "Server=x;NoSuchOption=1;Database=idi_loadtest"))
            .Problems.ShouldHaveSingleItem().ShouldContain("not a valid MySQL connection string");
    }

    [Fact]
    public void Lists_every_missing_setting()
    {
        var ex = Should.Throw<SettingsException>(() => Read(connection: null, hostname: " ", instance: "not-a-guid"));

        ex.Problems.Count.ShouldBe(3);
    }

    [Fact]
    public void Refuses_an_empty_instance_id()
    {
        Should.Throw<SettingsException>(() => Read(instance: Guid.Empty.ToString()));
    }

    [Fact]
    public void Describes_the_server_without_the_password()
    {
        var settings = Read();

        settings.Describe().ShouldBe("127.0.0.1:13306 as root");
        settings.Describe().ShouldNotContain("secret-pw");
    }

    [Fact]
    public void The_server_connection_selects_no_database()
    {
        Read().ServerConnectionString.ShouldNotContain("idi_loadtest", Case.Insensitive);
    }
}
