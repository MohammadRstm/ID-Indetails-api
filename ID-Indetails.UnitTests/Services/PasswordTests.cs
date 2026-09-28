using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Implementation;
using ID_Indetails.api.Services.Common.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class PasswordTests
{
    [Fact]
    public void Generated_passwords_have_every_character_class_and_differ()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => PasswordGenerator.Generate()).ToList();

        passwords.Distinct().Count().ShouldBe(50);
        passwords.ShouldAllBe(p => p.Length == 20
            && p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit) && p.Any(c => !char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void Generated_password_is_never_too_short()
        => Should.Throw<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(8));

    [Fact]
    public void Hash_verifies_the_right_password_only()
    {
        var service = new PasswordService();
        var user = new User();
        user.PasswordHash = service.Hash(user, "correct horse battery");

        user.PasswordHash.ShouldNotContain("correct horse battery");
        service.Verify(user, "correct horse battery").ShouldBe(PasswordCheck.Success);
        service.Verify(user, "wrong password").ShouldBe(PasswordCheck.Failed);
    }

    [Fact]
    public void User_without_a_password_never_verifies()
        => new PasswordService().Verify(new User(), "anything").ShouldBe(PasswordCheck.Failed);
}
