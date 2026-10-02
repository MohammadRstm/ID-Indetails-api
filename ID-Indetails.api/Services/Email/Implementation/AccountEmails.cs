using System.Globalization;
using System.Net;
using ID_Indetails.api.Services.Email.Abstraction;

namespace ID_Indetails.api.Services.Email.Implementation;

/// <summary>Content of the account emails. Every user-supplied value is HTML-encoded.</summary>
public static class AccountEmails
{
    public static EmailMessage Invitation(
        string productName,
        string toAddress,
        string inviterName,
        string roleLabel,
        string acceptUrl,
        DateTime expiresAtUtc)
    {
        var expires = expiresAtUtc.ToString("dddd d MMMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var subject = $"You're invited to {productName}";

        var html = $"""
            <p>Hello,</p>
            <p>{Encode(inviterName)} has invited you to join <strong>{Encode(productName)}</strong> as {Encode(roleLabel)}.</p>
            <p><a href="{Encode(acceptUrl)}">Create your account</a></p>
            <p>This link works once and expires on {Encode(expires)}.<br>
            If you weren't expecting this invitation, you can ignore this email.</p>
            """;

        var text = $"""
            Hello,

            {inviterName} has invited you to join {productName} as {roleLabel}.

            Create your account: {acceptUrl}

            This link works once and expires on {expires}.
            If you weren't expecting this invitation, you can ignore this email.
            """;

        return new EmailMessage(toAddress, null, subject, html, text);
    }

    public static EmailMessage SuperAdminCredentials(
        string productName,
        string toAddress,
        string firstName,
        string temporaryPassword,
        string loginUrl)
    {
        var subject = $"Your {productName} super admin account";

        var html = $"""
            <p>Hello {Encode(firstName)},</p>
            <p>A super admin account has been created for you on <strong>{Encode(productName)}</strong>.</p>
            <p>Sign in at <a href="{Encode(loginUrl)}">{Encode(loginUrl)}</a> with this email address and the temporary password:</p>
            <p style="font-family:monospace;font-size:16px">{Encode(temporaryPassword)}</p>
            <p>You will be asked to choose your own password when you first sign in.
            <strong>Once you have, delete this email</strong>, since it contains a password.</p>
            """;

        var text = $"""
            Hello {firstName},

            A super admin account has been created for you on {productName}.

            Sign in at {loginUrl} with this email address and the temporary password:

                {temporaryPassword}

            You will be asked to choose your own password when you first sign in.
            Once you have, delete this email, since it contains a password.
            """;

        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    public static EmailMessage PasswordReset(
        string productName,
        string toAddress,
        string? firstName,
        string resetUrl,
        DateTime expiresAtUtc)
    {
        var expires = expiresAtUtc.ToString("dddd d MMMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hello," : $"Hello {firstName},";
        var subject = $"Reset your {productName} password";

        var html = $"""
            <p>{Encode(greeting)}</p>
            <p>Someone asked to reset the password of your <strong>{Encode(productName)}</strong> account. If it was you, choose a new one:</p>
            <p><a href="{Encode(resetUrl)}">Choose a new password</a></p>
            <p>This link works once and expires on {Encode(expires)}. Choosing a new password signs you out everywhere.<br>
            If you didn't ask for this, you can ignore this email: your password stays as it is.</p>
            """;

        var text = $"""
            {greeting}

            Someone asked to reset the password of your {productName} account. If it was you, choose a new one:

            {resetUrl}

            This link works once and expires on {expires}. Choosing a new password signs you out everywhere.
            If you didn't ask for this, you can ignore this email: your password stays as it is.
            """;

        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    /// <summary>To the new sign-in address: from now on, this is the one to sign in with.</summary>
    public static EmailMessage SignInAddressSet(string productName, string toAddress, string? firstName, string loginUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hello," : $"Hello {firstName},";
        var subject = $"Your {productName} sign-in address has changed";

        var html = $"""
            <p>{Encode(greeting)}</p>
            <p>You now sign in to <strong>{Encode(productName)}</strong> with this email address, and your usual password.</p>
            <p><a href="{Encode(loginUrl)}">Sign in</a></p>
            <p>Anywhere you were signed in with your old address has been signed out.</p>
            """;

        var text = $"""
            {greeting}

            You now sign in to {productName} with this email address, and your usual password.

            Sign in: {loginUrl}

            Anywhere you were signed in with your old address has been signed out.
            """;

        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    /// <summary>
    /// To the old sign-in address, which no longer signs in. It doesn't say the new one: whoever reads this mailbox now
    /// may not be the person.
    /// </summary>
    public static EmailMessage SignInAddressReplaced(string productName, string toAddress, string? firstName)
    {
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hello," : $"Hello {firstName},";
        var subject = $"Your {productName} sign-in address has changed";

        var html = $"""
            <p>{Encode(greeting)}</p>
            <p>Your <strong>{Encode(productName)}</strong> account now signs in with a different email address. This one no longer signs in.</p>
            <p>If you didn't expect this, contact a super admin of your company.</p>
            """;

        var text = $"""
            {greeting}

            Your {productName} account now signs in with a different email address. This one no longer signs in.

            If you didn't expect this, contact a super admin of your company.
            """;

        return new EmailMessage(toAddress, firstName, subject, html, text);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
