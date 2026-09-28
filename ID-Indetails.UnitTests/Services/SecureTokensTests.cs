using ID_Indetails.api.Services.Common.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class SecureTokensTests
{
    [Fact]
    public void New_tokens_are_random_and_url_safe()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => SecureTokens.NewToken()).ToList();

        tokens.Distinct().Count().ShouldBe(100);
        tokens.ShouldAllBe(t => t.Length >= 43 && t.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
    }

    [Fact]
    public void Hash_is_deterministic_hex_sha256()
    {
        SecureTokens.Hash("abc").ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        SecureTokens.Hash("abc").ShouldBe(SecureTokens.Hash("abc"));
    }

    [Fact]
    public void Refresh_token_database_value_is_the_hash_of_the_cookie_value_and_differs_from_it()
    {
        var (cookieValue, storedHash) = SecureTokens.NewRefreshToken();

        storedHash.ShouldBe(SecureTokens.Hash(cookieValue));
        storedHash.ShouldNotBe(cookieValue);
        storedHash.Length.ShouldBe(64);
    }

    [Fact]
    public void Fixed_time_equals_compares_values()
    {
        SecureTokens.FixedTimeEquals("setup-key", "setup-key").ShouldBeTrue();
        SecureTokens.FixedTimeEquals("setup-key", "setup-kez").ShouldBeFalse();
        SecureTokens.FixedTimeEquals("", "setup-key").ShouldBeFalse();
    }
}
