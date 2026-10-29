using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonHistoryRulesTests
{
    [Theory]
    [InlineData("How is Villa Mounir doing?", "How is Villa Mounir doing?")]
    [InlineData("  How is\n  Villa   Mounir doing? ", "How is Villa Mounir doing?")]
    [InlineData("   ", "New chat")]
    [InlineData("Compare the hours logged by each employee this month with last month, by project", "Compare the hours logged by each employee this month with…")]
    public void A_chat_is_named_after_its_first_question_on_one_line(string question, string title)
        => ProtonHistoryRules.TitleOf(question).ShouldBe(title);

    [Fact]
    public void A_long_word_is_cut_where_it_must_be()
    {
        var title = ProtonHistoryRules.TitleOf(new string('a', 100));
        title.ShouldBe(new string('a', ProtonHistoryRules.TitleLength) + "…");
    }

    [Fact]
    public void The_access_a_chat_was_held_under_is_kept_as_a_short_hash()
    {
        var key = ProtonHistoryRules.AccessKey(new string('x', 2_000));
        key.Length.ShouldBe(64);
        key.ShouldBe(ProtonHistoryRules.AccessKey(new string('x', 2_000)));
        key.ShouldNotBe(ProtonHistoryRules.AccessKey(new string('x', 1_999)));
    }

    private static UserAccess Access(string[] permissions, string[] domains)
        => new(1, RoleCodes.Admin, DomainCodes.Design, permissions.ToList(), false, domains);

    [Fact]
    public void A_chat_is_hidden_only_when_access_was_lost()
    {
        var then = new[] { PermissionCodes.ProtonUse, PermissionCodes.ReportView };
        var domains = new[] { DomainCodes.Design };

        ProtonHistoryRules.LostAccess(then, domains, Access([.. then], domains)).ShouldBeFalse();
        // more access hides nothing
        ProtonHistoryRules.LostAccess(then, domains, Access([.. then, PermissionCodes.FinancialView], [DomainCodes.Design, DomainCodes.Execution])).ShouldBeFalse();
        // a permission or a domain lost does
        ProtonHistoryRules.LostAccess(then, domains, Access([PermissionCodes.ProtonUse], domains)).ShouldBeTrue();
        ProtonHistoryRules.LostAccess(then, domains, Access([.. then], [DomainCodes.Execution])).ShouldBeTrue();
        // a permission since removed from the catalog can't have been taken away
        ProtonHistoryRules.LostAccess([.. then, "statement:send"], domains, Access([.. then], domains)).ShouldBeFalse();
    }
}
