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

    /// <summary>A surface brings its own page, then the preferences follow; a refresh only when nothing else moves.</summary>
    [Fact]
    public void A_reply_runs_one_place_then_its_preferences_in_order()
    {
        var page = ProtonUiCommand.Open(Villa);
        var form = new ProtonUiCommand(ProtonUiCommandType.OpenSurface, "the project's details form", 12, "design", Surface: ProtonSurface.EditProject);
        var light = new ProtonUiCommand(ProtonUiCommandType.SetPreference, "light mode", Theme: ProtonTheme.Light);
        var dark = new ProtonUiCommand(ProtonUiCommandType.SetPreference, "dark mode", Theme: ProtonTheme.Dark);
        var quiet = new ProtonUiCommand(ProtonUiCommandType.SetPreference, "message sounds off", Sounds: false);

        ProtonUiRules.Pick([quiet, form, light, page, dark]).ShouldBe([form, dark, quiet]);
        ProtonUiRules.Pick([ProtonUiCommand.Refresh, light]).ShouldBe([light]);
        dark.Navigates.ShouldBeFalse();
        form.Navigates.ShouldBeTrue();
    }

    /// <summary>
    /// Every surface is described, and none of them is a one-click confirm: completing, voiding, deleting,
    /// deactivating, granting access or topping up are proposed on a card, never opened.
    /// </summary>
    [Fact]
    public void Every_surface_has_its_rules_and_none_is_a_confirm_dialog()
    {
        ProtonSurfaces.All.Keys.ShouldBe(Enum.GetValues<ProtonSurface>(), ignoreOrder: true);
        string[] confirms = ["Complete", "Void", "Delete", "Deactivate", "Discard", "TopUp", "Permission", "Role", "Move", "Session"];
        Enum.GetNames<ProtonSurface>().ShouldAllBe(name => !confirms.Any(name.Contains));

        // what emails someone needs the user's own words: the invites and the bug report
        ProtonSurfaces.All.Where(s => s.Value.EmailsSomeone).Select(s => s.Key)
            .ShouldBe([ProtonSurface.InvitePerson, ProtonSurface.InviteWebsiteStaff, ProtonSurface.ReportBug], ignoreOrder: true);
        ProtonSurfaces.All.Values.Where(s => s.Endpoint is not null).ShouldAllBe(s => s.Endpoint!.Policies.Count > 0);
    }

    /// <summary>The endpoints the navigation tools name all resolve: a name that doesn't is found here, not on a user's question.</summary>
    [Theory]
    [InlineData(typeof(OpenPageTool))]
    [InlineData(typeof(OpenSurfaceTool))]
    [InlineData(typeof(OpenRecordTool))]
    [InlineData(typeof(FindTool))]
    [InlineData(typeof(ProtonRecords))]
    [InlineData(typeof(ProtonPortalTab))]
    public void Every_endpoint_the_navigation_tools_name_resolves(Type tool)
        => Should.NotThrow(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(tool.TypeHandle));

    [Fact]
    public void Opening_a_record_carries_its_id_domain_and_tab_but_never_an_address()
    {
        var command = ProtonUiCommand.Open(Villa, ProtonProjectSection.Requirements);

        command.ShouldBe(new ProtonUiCommand(ProtonUiCommandType.OpenProject, "Villa Mounir", 12, "design", ProtonProjectSection.Requirements));
        ProtonUiCommand.Open(Villa, ProtonProjectSection.Overview).Section.ShouldBeNull();
        ProtonUiCommand.Open(new ProtonRef(ProtonRefType.Person, 7, "Rana", null)).Type.ShouldBe(ProtonUiCommandType.OpenPerson);
        ProtonUiCommand.Open(Villa, ProtonProjectSection.Portal, ProtonProjectPart.Discussion).Part.ShouldBe(ProtonProjectPart.Discussion);

        // a website project has no page of its own: it opens in its form, in the Website area
        var shown = ProtonUiCommand.Open(new ProtonRef(ProtonRefType.WebsiteProject, 4, "Sea house", "website"));
        shown.Type.ShouldBe(ProtonUiCommandType.OpenSurface);
        shown.Surface.ShouldBe(ProtonSurface.EditWebsiteProject);
        (shown.Id, shown.Domain).ShouldBe((4L, "website"));

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
