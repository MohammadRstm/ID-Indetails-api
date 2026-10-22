using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Staff sign-in pauses for an account after repeated wrong passwords (<see cref="SignInLockout"/>).</summary>
public class SignInLockoutTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task Sign_in_pauses_after_repeated_wrong_passwords_and_says_nothing_more()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var unknownError = await (await Client.PostJsonAsync("/api/auth/login", new { email = AuthTestSteps.NewEmail("nobody"), password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);

        for (var i = 0; i < SignInLockout.Attempts; i++)
        {
            await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "wrong-password-123" }))
                .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        }

        // Paused, on the website and the phone app: even the right password gets the same answer as an unknown email.
        var paused = await (await Client.PostJsonAsync("/api/auth/login", new { email, password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        paused.Message.ShouldBe(unknownError.Message);
        await (await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);

        await _steps.ExecuteAsync("UPDATE users SET locked_until = UTC_TIMESTAMP(3) - INTERVAL 1 MINUTE WHERE email = @e", ("@e", email));
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password })).ShouldSignInAsync();
        (await _steps.ScalarAsync<int>("SELECT failed_sign_ins FROM users WHERE email = @e", ("@e", email))).ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_passwords_sent_at_once_all_count_towards_the_pause()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var attempts = await Task.WhenAll(Enumerable.Range(0, SignInLockout.Attempts).Select(_ =>
            Client.PostJsonAsync("/api/auth/login", new { email, password = "wrong-password-123" })));
        attempts.ShouldAllBe(r => (int)r.StatusCode == 401);

        await (await Client.PostJsonAsync("/api/auth/login", new { email, password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Resetting_the_password_ends_the_pause_so_nobody_can_keep_a_person_out()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, _, _) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        // someone who knows the address keeps guessing: the account is paused
        for (var i = 0; i < SignInLockout.Attempts; i++)
        {
            await (await Client.PostJsonAsync("/api/auth/login", new { email, password = "wrong-password-123" }))
                .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        }

        // the person resets their password from their email, and can sign in with it at once
        (await Client.PostJsonAsync("/api/auth/forgot-password", new { email })).IsSuccessStatusCode.ShouldBeTrue();
        const string newPassword = "a-new-long-passphrase";
        (await Client.PostJsonAsync("/api/auth/reset-password", new { token = api.Emails.ResetTokenFor(email), password = newPassword }))
            .IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostJsonAsync("/api/auth/login", new { email, password = newPassword })).ShouldSignInAsync();
    }
}
