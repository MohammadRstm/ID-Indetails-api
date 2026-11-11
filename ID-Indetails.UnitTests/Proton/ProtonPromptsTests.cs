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

    /// <summary>Profit means three things in the app: the prompt says which field is which, in the tools' own names.</summary>
    [Fact]
    public void The_chat_prompt_says_what_each_money_figure_means()
    {
        ProtonPrompts.Chat.ShouldContain("# Money: what each figure means");
        ProtonPrompts.Chat.ShouldContain("`agreed_profit`");
        ProtonPrompts.Chat.ShouldContain("`period_cash_profit`");
        ProtonPrompts.Chat.ShouldContain("`site_cash_profit`");
        ProtonPrompts.Chat.ShouldNotContain("`cash_profit`");
        ProtonPrompts.Chat.ShouldContain("It is not a forecast");
        ProtonPrompts.Chat.ShouldContain("Billing figures are as of today");
    }

    /// <summary>
    /// A project's profitability: the bridge in order, a judgement only with its rule, what was left out, never one
    /// person's pay, and a simulation that is no forecast and saves nothing.
    /// </summary>
    [Fact]
    public void The_chat_prompt_says_how_to_present_a_projects_profitability_and_a_simulation()
    {
        ProtonPrompts.Chat.ShouldContain("# A project's profitability and what-if simulations");
        ProtonPrompts.Chat.ShouldContain("`get_project_financial_summary`");
        ProtonPrompts.Chat.ShouldContain("`simulate_project_financial_scenario`");
        ProtonPrompts.Chat.ShouldContain("from the base price to `agreed_profit`");
        ProtonPrompts.Chat.ShouldContain("A judgement needs its rule");
        ProtonPrompts.Chat.ShouldContain("`basis.omitted`");
        ProtonPrompts.Chat.ShouldContain("one person's pay");
        ProtonPrompts.Chat.ShouldContain("not a forecast or a promise");
        ProtonPrompts.Chat.ShouldContain("nothing was");
        // the terms, defined with the other money figures
        ProtonPrompts.Chat.ShouldContain("- `bridge`:");
        ProtonPrompts.Chat.ShouldContain("- `standing`:");
    }

    /// <summary>
    /// A business's risks and spending: each judgement with the rule that fired, nothing at risk or unusual without one,
    /// a projection as an estimate, internal projects never listed, and what was left out said.
    /// </summary>
    [Fact]
    public void The_chat_prompt_says_how_to_present_a_businesss_risks_and_spending()
    {
        ProtonPrompts.Chat.ShouldContain("# A business's financial risks and its spending");
        ProtonPrompts.Chat.ShouldContain("`detect_project_financial_risks`");
        ProtonPrompts.Chat.ShouldContain("`analyze_expense_trends`");
        ProtonPrompts.Chat.ShouldContain("Say the rule each risk names");
        ProtonPrompts.Chat.ShouldContain("Never call a project at risk, losing money or in trouble without a fired rule");
        ProtonPrompts.Chat.ShouldContain("A projection is an estimate");
        ProtonPrompts.Chat.ShouldContain("Internal projects are never listed");
        ProtonPrompts.Chat.ShouldContain("Spending is unusual only when a rule fired");
        ProtonPrompts.Chat.ShouldContain("`unconverted`");
        ProtonPrompts.Chat.ShouldContain("too short a history");
        // "unusual" now has two sources, and the analysis rules say so
        ProtonPrompts.Chat.ShouldContain("or a spending anomaly `analyze_expense_trends` lists");
        // the terms, defined with the other money figures
        ProtonPrompts.Chat.ShouldContain("- Risk register:");
        ProtonPrompts.Chat.ShouldContain("- `baseline`:");
        ProtonPrompts.Chat.ShouldContain("- Runway:");
    }

    /// <summary>
    /// Design's labour is never a person's, not even a department of one, and its outside-scope share is quoted as given;
    /// utilization is hours only, never a judgement of a person and never linked to pay.
    /// </summary>
    [Fact]
    public void The_chat_prompt_says_how_to_present_labour_cost_and_utilization()
    {
        ProtonPrompts.Chat.ShouldContain("# Design's labour cost");
        ProtonPrompts.Chat.ShouldContain("`analyze_labor_costs`");
        ProtonPrompts.Chat.ShouldContain("Labour is never per person");
        ProtonPrompts.Chat.ShouldContain("a department's labour is still never a person's pay");
        ProtonPrompts.Chat.ShouldContain("`outside_scope_pct`");
        ProtonPrompts.Chat.ShouldContain("# Utilization");
        ProtonPrompts.Chat.ShouldContain("`analyze_employee_utilization`");
        ProtonPrompts.Chat.ShouldContain("Utilization is logged ÷ (expected − excused)");
        ProtonPrompts.Chat.ShouldContain("A utilization figure is recorded time, not a judgement of a person");
        ProtonPrompts.Chat.ShouldContain("Never link utilization to pay");
        // the terms, defined with the other money figures
        ProtonPrompts.Chat.ShouldContain("- Outside scope:");
        ProtonPrompts.Chat.ShouldContain("- Utilization:");
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
