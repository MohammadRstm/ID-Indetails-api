using Anthropic.Models.Messages;
using ID_Indetails.api.Proton.Conversation;
using ID_Indetails.api.Proton.Tools;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonConversationTests
{
    [Fact]
    public void A_rollover_keeps_the_id_and_what_the_tools_found_and_recaps_the_last_three_exchanges()
    {
        var conversation = new ProtonConversation(Guid.NewGuid(), 7, "access");
        var alpha = new ProtonRef(ProtonRefType.Project, 4, "Alpha", "design");
        conversation.Known[(alpha.Type, alpha.Id)] = alpha;
        conversation.Messages.Add(new MessageParam { Role = Role.User, Content = "Hi" });
        conversation.Questions = 20;
        conversation.LastInputTokens = 90_000;
        for (var i = 1; i <= 4; i++)
        {
            conversation.RememberExchange($"Question {i}", $"Answer {i}");
        }

        conversation.RollOver();

        conversation.Messages.ShouldBeEmpty();
        conversation.Questions.ShouldBe(0);
        conversation.LastInputTokens.ShouldBe(0);
        conversation.Known.ShouldContainKey((ProtonRefType.Project, 4L));
        conversation.Recap.ShouldNotBeNull();
        conversation.Recap.ShouldNotContain("Question 1");
        conversation.Recap.ShouldContain("The user asked: \"Question 2\" You answered: \"Answer 2\"");
        conversation.Recap.ShouldContain("Question 4");

        // once a question after the rollover is answered, the recap is in the messages and isn't sent again
        conversation.RememberExchange("Question 5", "Answer 5");
        conversation.Recap.ShouldBeNull();
    }

    [Fact]
    public void A_recap_keeps_each_text_short_and_a_chat_with_nothing_said_has_none()
    {
        var empty = new ProtonConversation(Guid.NewGuid(), 7, "access");
        empty.RollOver();
        empty.Recap.ShouldBeNull();

        var conversation = new ProtonConversation(Guid.NewGuid(), 7, "access");
        conversation.RememberExchange(new string('q', 1_000), new string('a', 1_000));
        conversation.RollOver();
        conversation.Recap!.Length.ShouldBeLessThan(1_000);
    }
}
