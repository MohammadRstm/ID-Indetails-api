using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Actions;
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

    /// <summary>
    /// No form a fill can name carries a role, a password, a permission, bank details or a salary; a money field only
    /// when it is marked as one (filled only while money actions are on). Every form's values record is checked, rows
    /// included, so a new form can't slip past.
    /// </summary>
    [Fact]
    public void No_fill_can_express_access_or_secrets_and_money_only_where_marked()
    {
        string[] never = ["Role", "Password", "Permission", "Iban", "Swift", "Bank", "Salary", "Token", "Basis"];
        string[] money = ["Revenue", "Fee", "Markup", "Pay", "Amount", "Rate", "Currency", "Cost", "Wage", "Price"];
        var seen = new HashSet<Type>();
        var fields = new List<System.Reflection.PropertyInfo>();
        void Walk(Type type)
        {
            if (!seen.Add(type))
            {
                return;
            }

            foreach (var property in type.GetProperties())
            {
                fields.Add(property);
                var inner = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments()[0] : property.PropertyType;
                if (inner.Namespace == typeof(ProtonFormFill).Namespace && inner.Name.EndsWith("Fill", StringComparison.Ordinal))
                {
                    Walk(inner);
                }
            }
        }

        foreach (var slot in typeof(ProtonFormFill).GetProperties().Where(p => p.PropertyType.Name.EndsWith("Fill", StringComparison.Ordinal)))
        {
            Walk(slot.PropertyType);
        }

        seen.Count.ShouldBeGreaterThan(Enum.GetValues<ProtonForm>().Length);
        fields.ShouldAllBe(p => !never.Any(p.Name.Contains), "a fill can't carry access, secrets, bank details or salaries");
        fields.Where(p => money.Any(p.Name.Contains))
            .ShouldAllBe(p => p.IsDefined(typeof(ProtonMoneyAttribute), false), "a money field must be marked [ProtonMoney]");
    }

    /// <summary>Every form has what filling it needs; the pay forms need pay-rule actions, the payment money actions.</summary>
    [Fact]
    public void Every_form_says_what_filling_it_needs()
    {
        ProtonFormSpecs.All.Keys.ShouldBe(Enum.GetValues<ProtonForm>(), ignoreOrder: true);
        ProtonFormSpecs.All[ProtonForm.Schedule].Family.ShouldBe(ID_Indetails.api.Proton.Actions.ProtonFamily.PayRules);
        ProtonFormSpecs.All[ProtonForm.HourExcuse].Family.ShouldBe(ID_Indetails.api.Proton.Actions.ProtonFamily.PayRules);
        ProtonFormSpecs.All[ProtonForm.Payment].Family.ShouldBe(ID_Indetails.api.Proton.Actions.ProtonFamily.Money);
        // what reaches clients, the public website or someone's inbox
        ProtonFormSpecs.All.Where(s => s.Value.TypedOnly).Select(s => s.Key).ShouldBe(
        [
            ProtonForm.ReportBug, ProtonForm.Invite, ProtonForm.Message, ProtonForm.WorkEntryComment, ProtonForm.FileComment, ProtonForm.PortalIntro,
            ProtonForm.PortalMessage, ProtonForm.WebsiteProject, ProtonForm.WebsiteService, ProtonForm.WebsiteLocation, ProtonForm.WebsiteSocialLink,
        ], ignoreOrder: true);
    }

    [Fact]
    public void Money_is_found_in_a_fills_rows_and_matched_to_what_the_user_typed()
    {
        var invoice = new InvoiceFill { ProjectId = 1, Lines = [new InvoiceLineFill { Description = "Concept", Amount = 1500m }, new InvoiceLineFill { Description = "Drawings" }] };

        ProtonFormRules.MoneyIn(invoice).ShouldBe([("Amount", 1500m)]);
        ProtonFormRules.MoneyIn(new NewProjectFill { Title = "Villa" }).ShouldBeEmpty();
        // forms read the user's figures as money cards do (MoneyText)
        MoneyText.TypedFigures("Invoice them 1,500 for the concept").ShouldContain(1500m);
        MoneyText.TypedFigures("Invoice them 1500.00").ShouldContain(1500m);
        MoneyText.TypedFigures("Invoice them 1.5k").ShouldContain(1500m);
        MoneyText.TypedFigures("Invoice them 15000").ShouldNotContain(1500m);
        MoneyText.TypedFigures("Invoice them what the last one was").ShouldBeEmpty();
    }

    [Fact]
    public void The_newer_forms_check_their_own_shapes()
    {
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Requirement, Requirement: new RequirementFill { Text = "Order tiles" })).ShouldHaveSingleItem().ShouldContain("project_id");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.WebsiteSocialLink, WebsiteSocialLink: new WebsiteSocialLinkFill { Url = "http://x.test" }))
            .ShouldHaveSingleItem().ShouldContain("https");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.HourExcuse, 3, HourExcuse: new HourExcuseFill { Hours = 4, Days = [new DateOnly(2026, 10, 1)] }))
            .ShouldHaveSingleItem().ShouldContain("not both");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Schedule, 3, Schedule: new ScheduleFill { Days = [new ScheduleDayFill { Weekday = 1, StartMinute = 600, EndMinute = 540 }] }))
            .ShouldHaveSingleItem().ShouldContain("end after the start");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Materials, 9, Materials: new MaterialsFill())).ShouldHaveSingleItem().ShouldContain("rows");
        ProtonFormRules.Problems(new ProtonFormFill(ProtonForm.Payment, 9, Payment: new PaymentFill { Currency = "EUR" })).ShouldHaveSingleItem().ShouldContain("USD or LBP");
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
