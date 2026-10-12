using ID_Indetails.api.Proton.Actions;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

/// <summary>Who Proton may message (only someone the user named) and when a message needs the typed confirmation.</summary>
public class ProtonMessageTextTests
{
    [Theory]
    [InlineData("Rana Haddad", "Tell Rana the plans are ready", true)]
    [InlineData("Rana Haddad", "message haddad about Friday", true)]
    [InlineData("Design Team", "post in the design team chat", true)]
    [InlineData("Rana Haddad", "Reply to whoever asked about the plans", false)]
    [InlineData("Al Ranad", "tell rana", false)]
    [InlineData("Jo Li", "tell jo", false)]
    public void A_recipient_must_be_named_by_the_user(string name, string typed, bool named)
        => MessageText.Named(name, typed).ShouldBe(named);

    [Theory]
    [InlineData("The fee is $4,500.", true)]
    [InlineData("We're 20% over budget", true)]
    [InlineData("It came to 12,000", true)]
    [InlineData("Paid 300 USD today", true)]
    [InlineData("Can you send the plans by Friday 10 October?", false)]
    [InlineData("Meeting at 14:30 in room 2", false)]
    public void Figures_are_spotted(string body, bool figures) => MessageText.HasFigures(body).ShouldBe(figures);
}
