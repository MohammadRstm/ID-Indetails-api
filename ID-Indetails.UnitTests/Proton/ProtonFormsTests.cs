using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonFormsTests
{
    [Fact]
    public void A_fill_gives_one_forms_values_within_their_limits()
    {
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Client, Client: new ClientFill { Name = "Acme", Email = "hi@acme.com" })).ShouldBeEmpty();

        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Client, Client: new ClientFill { Email = "not-an-email" }))
            .ShouldHaveSingleItem().ShouldContain("email");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Client, Client: new ClientFill { Name = new string('x', 201) }))
            .ShouldHaveSingleItem().ShouldContain("name");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Meeting, Meeting: new MeetingFill { StartMinute = 600, EndMinute = 540 }))
            .ShouldHaveSingleItem().ShouldContain("end_minute");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Meeting, Meeting: new MeetingFill { StartMinute = 2000 }))
            .ShouldHaveSingleItem().ShouldContain("0 to 1439");
        // the values of another form, or none, aren't this form's
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Meeting, Client: new ClientFill { Name = "Acme" })).ShouldNotBeEmpty();
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Message)).ShouldNotBeEmpty();
    }

    /// <summary>No form a fill can name carries money, pay, a role, a password or a permission.</summary>
    [Fact]
    public void No_fill_can_express_money_pay_access_or_secrets()
    {
        string[] forbidden = ["Revenue", "Fee", "Markup", "Pay", "Amount", "Salary", "Rate", "Role", "Password", "Permission", "Currency", "Basis"];
        var fields = new[] { typeof(ClientFill), typeof(MeetingFill), typeof(NewProjectFill), typeof(EditProjectFill), typeof(ReportBugFill), typeof(InviteFill), typeof(MessageFill) }
            .SelectMany(t => t.GetProperties().Select(p => p.Name));

        fields.ShouldAllBe(name => !forbidden.Any(name.Contains));
    }

    [Fact]
    public void Only_the_listed_endpoints_without_a_permission_can_stand_in_for_a_tool()
    {
        var contacts = ProtonEndpoint.OfRuleBased<MessagesController>(nameof(MessagesController.Contacts));
        contacts.Policies.ShouldBe([ID_Indetails.api.Authorization.AuthorizationPolicies.PermissionPrefix + ID_Indetails.api.Constants.PermissionCodes.ProtonUse]);
        contacts.Writes.ShouldBeFalse();

        // not listed, or not a read
        Should.Throw<InvalidOperationException>(() => ProtonEndpoint.OfRuleBased<MessagesController>(nameof(MessagesController.Badge)));
        Should.Throw<InvalidOperationException>(() => ProtonEndpoint.OfRuleBased<MessagesController>(nameof(MessagesController.SendDirect)));
    }
}
