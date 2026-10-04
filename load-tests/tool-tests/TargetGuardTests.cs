using ID_Indetails.LoadTests.Tool.Safety;
using Shouldly;

namespace ID_Indetails.LoadTests.Tool.Tests;

public class TargetGuardTests
{
    private static readonly Guid Instance = Guid.NewGuid();

    private static readonly LoadTestSettings Settings = new(
        "Server=127.0.0.1;Port=13306;User=root;Password=x;Database=idi_loadtest;", "idi-loadtest-db", Instance);

    private static ServerIdentity Server(
        string hostname = "idi-loadtest-db", IdentityMarker? marker = null, int tables = 0)
        => new(hostname, "8.4.6", marker, DatabaseExists: tables > 0, tables);

    private static readonly IdentityMarker OwnMarker = new(LoadTestNames.Environment, Instance);

    [Theory]
    [InlineData(GuardMode.Existing)]
    [InlineData(GuardMode.Initialise)]
    public void Allows_the_marked_load_test_server(GuardMode mode)
    {
        TargetGuard.Problems(Settings, Server(marker: OwnMarker, tables: 60), mode).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(GuardMode.Existing)]
    [InlineData(GuardMode.Initialise)]
    public void Refuses_a_server_with_another_hostname(GuardMode mode)
    {
        TargetGuard.Problems(Settings, Server(hostname: "4f3c2a1b0d9e", marker: OwnMarker), mode)
            .ShouldHaveSingleItem().ShouldContain("not the load-test database server");
    }

    [Fact]
    public void Refuses_an_unmarked_server_for_everything_but_init()
    {
        TargetGuard.Problems(Settings, Server(), GuardMode.Existing)
            .ShouldHaveSingleItem().ShouldContain("no load-test identity");
    }

    [Fact]
    public void Init_marks_an_unmarked_server_whose_database_is_empty()
    {
        TargetGuard.Problems(Settings, Server(), GuardMode.Initialise).ShouldBeEmpty();
    }

    [Fact]
    public void Init_refuses_an_unmarked_server_whose_database_already_has_tables()
    {
        TargetGuard.Problems(Settings, Server(tables: 12), GuardMode.Initialise)
            .ShouldHaveSingleItem().ShouldContain("won't mark or touch a database it didn't create");
    }

    [Theory]
    [InlineData(GuardMode.Existing)]
    [InlineData(GuardMode.Initialise)]
    public void Refuses_a_server_of_another_load_test_environment(GuardMode mode)
    {
        TargetGuard.Problems(Settings, Server(marker: OwnMarker with { InstanceId = Guid.NewGuid() }), mode)
            .ShouldHaveSingleItem().ShouldContain("belongs to load-test instance");
    }

    [Fact]
    public void Refuses_a_marker_that_isnt_a_load_test_one()
    {
        TargetGuard.Problems(Settings, Server(marker: OwnMarker with { Environment = "production" }), GuardMode.Existing)
            .ShouldHaveSingleItem().ShouldContain("'production'");
    }

    [Fact]
    public void Reports_every_problem_at_once()
    {
        TargetGuard.Problems(Settings, Server(hostname: "prod-db"), GuardMode.Existing).Count.ShouldBe(2);
    }
}
