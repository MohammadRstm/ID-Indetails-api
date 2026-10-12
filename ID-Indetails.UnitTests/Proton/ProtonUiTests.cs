using System.Reflection;
using FluentValidation.TestHelper;
using ID_Indetails.api.DTO.Request.Proton;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Validators.Proton;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonUiTests
{
    private static readonly ProtonRef Villa = new(ProtonRefType.Project, 12, "Villa Mounir", "design");
    private static readonly ProtonRef Acme = new(ProtonRefType.Client, 3, "Acme", "design");

    [Fact]
    public void A_reply_navigates_once_to_the_last_place_asked_for()
    {
        var first = ProtonUiCommand.Open(Villa);
        var second = ProtonUiCommand.Open(Acme);

        ProtonUiRules.Pick([first, ProtonUiCommand.Refresh, second]).ShouldBe([second]);
        ProtonUiRules.Pick([ProtonUiCommand.Refresh, ProtonUiCommand.Refresh]).ShouldBe([ProtonUiCommand.Refresh]);
        ProtonUiRules.Pick([]).ShouldBeEmpty();
    }

    [Fact]
    public void Opening_a_record_carries_its_id_domain_and_tab_but_never_an_address()
    {
        var command = ProtonUiCommand.Open(Villa, ProtonProjectSection.Requirements);

        command.ShouldBe(new ProtonUiCommand(ProtonUiCommandType.OpenProject, "Villa Mounir", 12, "design", ProtonProjectSection.Requirements));
        ProtonUiCommand.Open(Villa, ProtonProjectSection.Overview).Section.ShouldBeNull();
        ProtonUiCommand.Open(new ProtonRef(ProtonRefType.Person, 7, "Rana", null)).Type.ShouldBe(ProtonUiCommandType.OpenPerson);

        // only enums, ids, a domain code and the record's own name: nothing that could hold a URL or a script
        typeof(ProtonUiCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ShouldBe(["Label", "Domain"], ignoreOrder: true);
    }

    [Fact]
    public void A_page_context_names_known_places_only_and_a_record_with_its_kind()
    {
        var validator = new AskProtonRequestValidator();
        AskProtonRequest With(ProtonPageContext page) => new() { Text = "What about its financials?", Page = page };

        validator.TestValidate(With(new ProtonPageContext
        {
            Area = "design", Page = ProtonPage.Projects, EntityType = ProtonRefType.Project, EntityId = 12, Section = ProtonProjectSection.Requirements,
        })).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(With(new ProtonPageContext { Area = "company" })).ShouldNotHaveAnyValidationErrors();

        validator.TestValidate(With(new ProtonPageContext { Area = "javascript:alert(1)" })).IsValid.ShouldBeFalse();
        validator.TestValidate(With(new ProtonPageContext { Area = "design", EntityId = 12 })).IsValid.ShouldBeFalse();
        validator.TestValidate(With(new ProtonPageContext { Area = "design", EntityType = ProtonRefType.Project })).IsValid.ShouldBeFalse();
        validator.TestValidate(With(new ProtonPageContext { Area = "design", EntityType = ProtonRefType.Project, EntityId = -4 })).IsValid.ShouldBeFalse();
        validator.TestValidate(With(new ProtonPageContext { Area = "design", Page = (ProtonPage)99 })).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_pick_from_the_buttons_needs_no_text_but_a_real_option()
    {
        var validator = new AskProtonRequestValidator();

        validator.TestValidate(new AskProtonRequest { Choice = new ProtonChoiceRequest { Type = ProtonRefType.Project, Id = 12 } })
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new AskProtonRequest { Choice = new ProtonChoiceRequest { Type = ProtonRefType.Project, Id = 0 } }).IsValid.ShouldBeFalse();
        validator.TestValidate(new AskProtonRequest()).IsValid.ShouldBeFalse();
    }
}
