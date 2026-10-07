using System.Text.Json;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Proton.Tools;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonSafetyTests
{
    [Theory]
    [InlineData("SELECT * FROM projects", true)]
    [InlineData("  select 1", true)]
    [InlineData("-- tag\nSELECT 1", true)]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x", true)]
    [InlineData("UPDATE projects SET title = 'x'", false)]
    [InlineData("INSERT INTO activity_events VALUES (1); SELECT LAST_INSERT_ID();", false)]
    [InlineData("DELETE FROM projects", false)]
    [InlineData("-- SELECT\nDROP TABLE projects", false)]
    public void Only_selects_count_as_reads(string sql, bool isRead)
        => ProtonReadOnly.IsRead(sql).ShouldBe(isRead);

    [Fact]
    public async Task Inside_a_tool_run_every_write_is_refused_and_outside_it_nothing_changes()
    {
        var guard = ProtonReadOnly.Interceptor;
        using var update = new MySqlCommand("UPDATE projects SET title = 'x'");
        using var select = new MySqlCommand("SELECT 1");

        // Outside: untouched.
        Should.NotThrow(() => guard.NonQueryExecuting(update, null!, default));

        await ProtonReadOnly.RunAsync(async () =>
        {
            await Task.Yield();
            Should.Throw<ProtonWriteRefusedException>(() => guard.NonQueryExecuting(update, null!, default));
            Should.Throw<ProtonWriteRefusedException>(() => guard.ReaderExecuting(update, null!, default));
            Should.Throw<ProtonWriteRefusedException>(() => guard.ScalarExecuting(update, null!, default));
            Should.NotThrow(() => guard.ReaderExecuting(select, null!, default));
            return 0;
        });

        ProtonReadOnly.IsActive.ShouldBeFalse();
        Should.NotThrow(() => guard.NonQueryExecuting(update, null!, default));
    }

    [Fact]
    public void Logged_tool_inputs_keep_ids_and_codes_but_only_the_length_of_free_text()
    {
        var input = JsonDocument.Parse("""{"project_id":42,"period":"last_month","from":"2026-09-01","search":"Villa Haddad renovation","overdue_only":true,"types":["project"]}""").RootElement;

        var summary = ProtonInputSummary.Of(input);

        summary.ShouldContain("\"project_id\":42");
        summary.ShouldContain("\"period\":\"last_month\"");
        summary.ShouldContain("\"from\":\"2026-09-01\"");
        summary.ShouldContain("\"search\":\"text(23)\"");
        summary.ShouldNotContain("Haddad");
        summary.ShouldContain("\"types\":\"list(1)\"");
    }

    [Fact]
    public void The_focus_follows_successful_tool_calls_about_exactly_one_record()
    {
        var focus = new ConversationFocus();
        var alpha = new ProtonRef(ProtonRefType.Project, 42, "Alpha", "design");
        var lina = new ProtonRef(ProtonRefType.Person, 7, "Lina");
        var september = new DayRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        focus.Take(Result("get_project", ProtonToolStatus.Ok, [alpha]));
        focus.Take(Result("get_project_team", ProtonToolStatus.Ok, [alpha, lina], september));
        // Several projects at once don't replace the one being discussed, and failures change nothing.
        focus.Take(Result("list_projects", ProtonToolStatus.Ok,
            [new ProtonRef(ProtonRefType.Project, 1, "One"), new ProtonRef(ProtonRefType.Project, 2, "Two")]));
        focus.Take(Result("get_project", ProtonToolStatus.NotFound, [new ProtonRef(ProtonRefType.Project, 9, "Hidden")]));

        focus.Project.ShouldBe(alpha);
        focus.People.ShouldBe([lina]);
        focus.Period.ShouldBe(september);
        focus.Describe().ShouldContain("project #42 \"Alpha\"");
        focus.Describe().ShouldContain("period 2026-09-01 to 2026-09-30");
        focus.Describe().ShouldNotContain("Hidden");
    }

    private static ProtonToolResult Result(string tool, ProtonToolStatus status, IReadOnlyList<ProtonRef> refs, DayRange? period = null)
        => new(tool, default, status, "{}", refs, period, 1);
}
