using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Clients;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ClientRulesTests
{
    private static ClientRules.ContactInput Email(string value, bool primary = false) => new(ContactKind.Email, value, null, false, primary);

    private static ClientRules.ContactInput Phone(string value, bool whatsapp = false) => new(ContactKind.Phone, value, " Office ", whatsapp, false);

    [Fact]
    public void Emails_ignore_case_and_spaces_and_phones_keep_only_digits()
    {
        ClientRules.Normalize(ContactKind.Email, "  Rami@Studio.COM ").ShouldBe("rami@studio.com");
        ClientRules.Normalize(ContactKind.Phone, "+961 (3) 123-456").ShouldBe("9613123456");
    }

    [Fact]
    public void The_first_contact_is_primary_unless_another_is_marked()
    {
        var (first, _) = ClientRules.PrepareContacts([Email("a@x.com"), Phone("03 111 222")]);
        first.Select(c => c.IsPrimary).ShouldBe([true, false]);

        var (marked, _) = ClientRules.PrepareContacts([Email("a@x.com"), new(ContactKind.Phone, "03 111 222", null, true, true)]);
        marked.Select(c => c.IsPrimary).ShouldBe([false, true]);
    }

    [Fact]
    public void Contacts_are_trimmed_and_only_phones_keep_whatsapp()
    {
        var (contacts, _) = ClientRules.PrepareContacts([Phone(" 03 111 222 ", whatsapp: true), new(ContactKind.Email, "b@x.com", "", true, false)]);

        contacts[0].Value.ShouldBe("03 111 222");
        contacts[0].Label.ShouldBe("Office");
        contacts[0].IsWhatsapp.ShouldBeTrue();
        contacts[1].Label.ShouldBeNull();
        contacts[1].IsWhatsapp.ShouldBeFalse();
    }

    [Fact]
    public void The_same_contact_cannot_be_listed_twice()
    {
        var (_, denial) = ClientRules.PrepareContacts([Email("a@x.com"), Email(" A@X.com")]);

        denial!.Code.ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void A_client_with_projects_cannot_be_deleted()
    {
        ClientRules.CheckDelete(0).ShouldBeNull();
        ClientRules.CheckDelete(2)!.Code.ShouldBe(ErrorCodes.ClientHasProjects);
    }

    [Fact]
    public void Archiving_and_restoring_must_change_something()
    {
        ClientRules.CheckArchive(isArchived: false, archive: true).ShouldBeNull();
        ClientRules.CheckArchive(isArchived: true, archive: false).ShouldBeNull();
        ClientRules.CheckArchive(true, true)!.Code.ShouldBe(ErrorCodes.ClientAlreadyArchived);
        ClientRules.CheckArchive(false, false)!.Code.ShouldBe(ErrorCodes.ClientNotArchived);
    }

    [Fact]
    public void Display_name_is_the_organisation_when_there_is_one()
    {
        ClientRules.DisplayName("Rami Khoury", "Cedar Studio").ShouldBe("Cedar Studio");
        ClientRules.DisplayName("Rami Khoury", " ").ShouldBe("Rami Khoury");
    }

    [Fact]
    public void A_client_needs_a_name_a_label_and_at_least_one_valid_contact()
    {
        var validator = new CreateClientRequestValidator();

        validator.Validate(new CreateClientRequest { Name = " ", Label = "", Contacts = [] })
            .Errors.Select(e => e.PropertyName).Distinct().ShouldBe(["Name", "Label", "Contacts"], ignoreOrder: true);

        validator.Validate(new CreateClientRequest
        {
            Name = "Rami",
            Label = "Client",
            Contacts =
            [
                new ClientContactRequest { Kind = ContactKind.Email, Value = "not-an-email" },
                new ClientContactRequest { Kind = ContactKind.Phone, Value = "12" },
                new ClientContactRequest { Kind = ContactKind.Email, Value = "a@x.com", IsWhatsapp = true },
            ],
        }).Errors.Count.ShouldBe(3);

        validator.Validate(new CreateClientRequest
        {
            Name = "Rami",
            Label = "Client",
            Contacts = [new ClientContactRequest { Kind = ContactKind.Phone, Value = "+961 3 123 456", IsWhatsapp = true }],
        }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Only_one_contact_can_be_marked_primary()
        => new UpdateClientRequestValidator().Validate(new UpdateClientRequest
        {
            Name = "Rami",
            Label = "Client",
            Contacts =
            [
                new ClientContactRequest { Kind = ContactKind.Email, Value = "a@x.com", IsPrimary = true },
                new ClientContactRequest { Kind = ContactKind.Email, Value = "b@x.com", IsPrimary = true },
            ],
        }).IsValid.ShouldBeFalse();
}
