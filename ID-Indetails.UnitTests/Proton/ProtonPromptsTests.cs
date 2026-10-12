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

    /// <summary>Charts and tables come from present_data, built from the data; never drawn in text or invented.</summary>
    [Fact]
    public void The_chat_prompt_shows_figures_only_through_present_data()
    {
        ProtonPrompts.Chat.ShouldContain("`present_data`");
        ProtonPrompts.Chat.ShouldContain("show the closest form that fits");
        ProtonPrompts.Chat.ShouldContain("never a table drawn in");
    }
}
