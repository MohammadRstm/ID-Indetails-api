using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Auth;
using ID_Indetails.api.Validators.Invitations;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class AuthValidatorTests
{
    [Fact]
    public void Login_requires_a_valid_email_and_a_password()
    {
        var result = new LoginRequestValidator().Validate(new LoginRequest { Email = "not-an-email", Password = "" });

        result.Errors.Select(e => e.PropertyName).ShouldBe(["Email", "Password"], ignoreOrder: true);
    }

    [Fact]
    public void New_password_must_be_long_enough_and_different()
    {
        var validator = new ChangePasswordRequestValidator();

        validator.Validate(new ChangePasswordRequest { CurrentPassword = "old-password-1", NewPassword = "short" })
            .IsValid.ShouldBeFalse();
        validator.Validate(new ChangePasswordRequest { CurrentPassword = "same-password-1", NewPassword = "same-password-1" })
            .IsValid.ShouldBeFalse();
        validator.Validate(new ChangePasswordRequest { CurrentPassword = "old-password-1", NewPassword = "a-much-longer-passphrase" })
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Invitation_rejects_unknown_roles_and_a_domain_for_super_admins()
    {
        var validator = new CreateInvitationRequestValidator();

        validator.Validate(new CreateInvitationRequest { Email = "a@b.co", Role = "owner" }).IsValid.ShouldBeFalse();
        validator.Validate(new CreateInvitationRequest { Email = "a@b.co", Role = "super_admin", DomainCode = "design" }).IsValid.ShouldBeFalse();
        validator.Validate(new CreateInvitationRequest { Email = "a@b.co", Role = "employee", DomainCode = "design" }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Invitation_pay_must_be_positive_with_a_three_letter_currency()
    {
        var validator = new CreateInvitationRequestValidator();
        var request = new CreateInvitationRequest
        {
            Email = "a@b.co",
            Role = "employee",
            DomainCode = "design",
            Pay = new InvitationPayRequest { Basis = PayBasis.Monthly, Amount = 0, Currency = "DOLLARS" },
        };

        var errors = validator.Validate(request).Errors.Select(e => e.PropertyName).ToList();

        errors.ShouldContain("Pay.Amount");
        errors.ShouldContain("Pay.Currency");
    }

    [Fact]
    public void Accepting_needs_names_a_strong_enough_password_and_a_plausible_phone()
    {
        var validator = new AcceptInvitationRequestValidator();

        validator.Validate(new AcceptInvitationRequest
        {
            Token = "t", FirstName = " ", LastName = "Haddad", Phone = "call me", Password = "short",
        }).Errors.Select(e => e.PropertyName).ShouldBe(["FirstName", "Phone", "Password"], ignoreOrder: true);

        validator.Validate(new AcceptInvitationRequest
        {
            Token = "t", FirstName = "Sara", LastName = "Haddad", Phone = "+961 70 123 456", Password = "a-good-passphrase",
        }).IsValid.ShouldBeTrue();
    }
}
