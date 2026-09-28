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

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
