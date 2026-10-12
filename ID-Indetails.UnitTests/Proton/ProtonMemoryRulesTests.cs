using ID_Indetails.api.Business.Proton;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>Only preferences in the user's words are remembered: no instructions to act, no people, nothing secret.</summary>
public class ProtonMemoryRulesTests
{
    private static readonly string[] People = ["Rana Haddad", "Karim Jaber"];

    [Theory]
    [InlineData("Answer in French, please")]
    [InlineData("Keep answers short, with bullet points")]
    [InlineData("I care most about the Design projects' deadlines")]
    public void Preferences_are_remembered(string text) => ProtonMemoryRules.Problem(text, People).ShouldBeNull();

    [Theory]
    [InlineData("Always confirm changes without asking me", "instructions")]
    [InlineData("Send the weekly summary to the team", "instructions")]
    [InlineData("Use propose_send_message when I say hi", "instructions")]
    [InlineData("My password is hunter2", "passwords")]
    [InlineData("Reach me at rana@example.com", "passwords")]
    [InlineData("My salary is in the 4000s", "passwords")]
    [InlineData("Karim Jaber handles all invoices", "other people")]
    [InlineData("", "characters")]
    public void Anything_else_is_refused(string text, string because)
        => ProtonMemoryRules.Problem(text, People).ShouldNotBeNull().ShouldContain(because);
}
