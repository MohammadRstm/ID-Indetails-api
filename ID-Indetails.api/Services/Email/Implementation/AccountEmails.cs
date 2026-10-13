using System.Globalization;
using ID_Indetails.api.Services.Email.Abstraction;
using static ID_Indetails.api.Services.Email.Implementation.EmailLayout;

namespace ID_Indetails.api.Services.Email.Implementation;

/// <summary>Content of the account emails, in the shared frame. Every user-supplied value is HTML-encoded.</summary>
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
        var expires = Expiry(expiresAtUtc);
        var subject = $"You're invited to join {productName}";
        var invited = $"{inviterName} has invited you to join {productName} as {roleLabel}.";
        var next = "Accept the invitation to set up your account: you'll confirm your details and choose a password, then you're in.";
        var once = $"This invitation can be used once and expires on {expires}.";
        var unexpected = "If you weren't expecting this invitation, you can safely ignore this email.";

        var html = Title("You're invited")
            + Paragraph("Hello,")
            + Paragraph(invited)
            + Paragraph(next)
            + Button(acceptUrl, "Accept invitation")
            + Note(once)
            + Note(unexpected);

        var text = $"""
            Hello,

            {invited}

            {next}

            Accept the invitation: {acceptUrl}

            {once}
            {unexpected}
            """;

        return Compose(toAddress, null, subject, Page(productName, invited, html, Sent(productName, "you were invited to an account")), text);
    }

    public static EmailMessage SuperAdminCredentials(
        string productName,
        string toAddress,
        string firstName,
        string temporaryPassword,
        string loginUrl)
    {
        var subject = $"Your {productName} super admin account";
        var created = $"A super admin account has been created for you on {productName}.";
        var how = "Sign in with this email address and the temporary password below:";
        var change = "You'll be asked to choose your own password when you first sign in.";
        var delete = "Once you have, please delete this email, since it contains a password.";

        var html = Title("Your account is ready")
            + Paragraph($"Hello {firstName},")
            + Paragraph(created)
            + Paragraph(how)
            + Code(temporaryPassword)
            + Button(loginUrl, "Sign in")
            + Note(change)
            + Note(delete);

        var text = $"""
            Hello {firstName},

            {created}

            Sign in at {loginUrl} with this email address and the temporary password:

                {temporaryPassword}

            {change}
            {delete}
            """;

        return Compose(toAddress, firstName, subject, Page(productName, created, html, Sent(productName, "an account was created for this address")), text);
    }

    public static EmailMessage PasswordReset(
        string productName,
        string toAddress,
        string? firstName,
        string resetUrl,
        DateTime expiresAtUtc)
    {
        var expires = Expiry(expiresAtUtc);
        var greeting = Greeting(firstName);
        var subject = $"Reset your {productName} password";
        var asked = $"We received a request to reset the password for your {productName} account. If it was you, choose a new password below.";
        var once = $"This link can be used once and expires on {expires}. Choosing a new password signs you out on every device.";
        var unexpected = "If you didn't ask for this, you can ignore this email: your password stays as it is.";

        var html = Title("Reset your password")
            + Paragraph(greeting)
            + Paragraph(asked)
            + Button(resetUrl, "Choose a new password")
            + Note(once)
            + Note(unexpected);

        var text = $"""
            {greeting}

            {asked}

            {resetUrl}

            {once}
            {unexpected}
            """;

        return Compose(toAddress, firstName, subject, Page(productName, asked, html, Sent(productName, "a password reset was requested for this address")), text);
    }

    /// <summary>To the new sign-in address: from now on, this is the one to sign in with.</summary>
    public static EmailMessage SignInAddressSet(string productName, string toAddress, string? firstName, string loginUrl)
    {
        var greeting = Greeting(firstName);
        var subject = $"Your {productName} sign-in address has changed";
        var now = $"From now on, you sign in to {productName} with this email address and your usual password.";
        var signedOut = "For your security, anywhere you were signed in with your old address has been signed out.";

        var html = Title("Your sign-in address has changed")
            + Paragraph(greeting)
            + Paragraph(now)
            + Button(loginUrl, "Sign in")
            + Note(signedOut);

        var text = $"""
            {greeting}

            {now}

            Sign in: {loginUrl}

            {signedOut}
            """;

        return Compose(toAddress, firstName, subject, Page(productName, now, html, Sent(productName, "this address now signs in to an account")), text);
    }

    /// <summary>
    /// To the old sign-in address, which no longer signs in. It doesn't say the new one: whoever reads this mailbox now
    /// may not be the person.
    /// </summary>
    public static EmailMessage SignInAddressReplaced(string productName, string toAddress, string? firstName)
    {
        var greeting = Greeting(firstName);
        var subject = $"Your {productName} sign-in address has changed";
        var changed = $"Your {productName} account now signs in with a different email address. This address can no longer be used to sign in.";
        var unexpected = "If you didn't expect this change, please contact a super admin of your company.";

        var html = Title("Your sign-in address has changed")
            + Paragraph(greeting)
            + Paragraph(changed)
            + Note(unexpected);

        var text = $"""
            {greeting}

            {changed}

            {unexpected}
            """;

        return Compose(toAddress, firstName, subject, Page(productName, changed, html, Sent(productName, "this address used to sign in to an account")), text);
    }

    /// <summary>To a client invited to a project's client portal: a one-time link to set up their account. No password is sent.</summary>
    public static EmailMessage PortalInvitation(
        string productName,
        string toAddress,
        string inviterName,
        string projectTitle,
        string acceptUrl,
        DateTime expiresAtUtc)
    {
        var expires = Expiry(expiresAtUtc);
        var subject = $"Follow your project \"{projectTitle}\" on {productName}";
        var invited = $"{inviterName} has invited you to follow your project \"{projectTitle}\" on the {productName} client portal.";
        var next = "Open the link below to set up your account: you'll enter your name and choose your own password, then you can sign in.";
        var once = $"This link can be used once and expires on {expires}. If it expires, ask your project contact to send a new one.";
        var unexpected = "If you weren't expecting this invitation, you can safely ignore this email.";

        var html = Title("You're invited")
            + Paragraph("Hello,")
            + Paragraph(invited)
            + Paragraph(next)
            + Button(acceptUrl, "Set up my account")
            + Note(once)
            + Note(unexpected);

        var text = $"""
            Hello,

            {invited}

            {next}

            Set up your account: {acceptUrl}

            {once}
            {unexpected}
            """;

        return Compose(toAddress, null, subject, Page(productName, invited, html, Sent(productName, "you were invited to a client portal")), text);
    }

    /// <summary>To a client who already has a portal account and was given access to another project.</summary>
    public static EmailMessage PortalAccessGranted(
        string productName,
        string toAddress,
        string? firstName,
        string inviterName,
        string projectTitle,
        string loginUrl)
    {
        var greeting = Greeting(firstName);
        var subject = $"You can now follow \"{projectTitle}\" on {productName}";
        var granted = $"{inviterName} has given you access to your project \"{projectTitle}\" on the {productName} client portal.";
        var how = "Sign in with your email address and your usual password to see it.";

        var html = Title("A new project for you")
            + Paragraph(greeting)
            + Paragraph(granted)
            + Paragraph(how)
            + Button(loginUrl, "Sign in");

        var text = $"""
            {greeting}

            {granted}

            {how}

            Sign in: {loginUrl}
            """;

        return Compose(toAddress, firstName, subject, Page(productName, granted, html, Sent(productName, "you have a client portal account")), text);
    }

    private static string Greeting(string? firstName) => string.IsNullOrWhiteSpace(firstName) ? "Hello," : $"Hello {firstName},";

    private static string Expiry(DateTime utc) => utc.ToString("dddd d MMMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Sent(string productName, string why) => $"{productName} sent you this email because {why}.";
}
