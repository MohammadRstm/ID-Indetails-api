using ID_Indetails.api.Proton.Prompts;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonPromptsTests
{
    [Fact]
    public void The_chat_prompt_starts_with_who_Proton_is_and_how_it_sounds()
    {
        ProtonPrompts.Chat.ShouldStartWith("# Who you are");
        ProtonPrompts.Chat.ShouldContain("You are an AI. Say so whenever someone asks");
        ProtonPrompts.Chat.ShouldContain("Reply in the language the user writes in");
    }

    /// <summary>Users know the app by its product name; the old working name must not reach the model.</summary>
    [Fact]
    public void The_app_is_named_by_its_product_name_only()
    {
        ProtonPrompts.Chat.ShouldNotContain("Hourline", Case.Insensitive);
        ProtonPrompts.Chat.ShouldContain(ProtonPrompts.ProductToken);

        var chat = ProtonPrompts.ChatFor("ID InDetails");
        chat.ShouldNotContain(ProtonPrompts.ProductToken);
        chat.ShouldContain("You are Proton, the AI assistant inside ID InDetails");
    }

    /// <summary>Revenue and profit mean two things in the app: the prompt says which field is which, in the tools' own names.</summary>
    [Fact]
    public void The_chat_prompt_says_what_each_money_figure_means()
    {
        ProtonPrompts.Chat.ShouldContain("# Money: what each figure means");
        ProtonPrompts.Chat.ShouldContain("`agreed_profit`");
        ProtonPrompts.Chat.ShouldContain("`cash_profit`");
        ProtonPrompts.Chat.ShouldContain("It is not a forecast");
        ProtonPrompts.Chat.ShouldContain("Billing figures are as of today");
    }

    /// <summary>Charts and tables come from present_data, built from the data; never drawn in text or invented.</summary>
    [Fact]
    public void The_chat_prompt_shows_figures_only_through_present_data()
    {
        ProtonPrompts.Chat.ShouldContain("`present_data`");
        ProtonPrompts.Chat.ShouldContain("show the closest form that fits");
        ProtonPrompts.Chat.ShouldContain("never a table drawn in");
    }
}
