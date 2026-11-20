using ID_Indetails.api.Authorization;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Support.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Support.Implementation;

/// <summary>
/// Messages to the developer go by email to <see cref="BugReportOptions.ToAddress"/>, and the sender gets a copy;
/// nothing is stored.
/// </summary>
public sealed class BugReportService(
    ICurrentUser currentUser,
    ICurrentPortalAccount currentPortalAccount,
    IUserRepository users,
    IPortalAccountRepository portalAccounts,
    IEmailSender emailSender,
    IClock clock,
    IOptions<BugReportOptions> options,
    IOptions<EmailOptions> emailOptions,
    ILogger<BugReportService> logger) : IBugReportService
{
    private const int MaxUserAgentLength = 300;

    public async Task SendAsync(ReportBugRequest request, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(currentUser.Id, cancellationToken)
            ?? throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "You need to sign in.");
        var sender = new ContactSender(
            $"{user.FirstName} {user.LastName}".Trim(), user.Email, $"user {user.Id}", $"{user.RoleCode} · {user.DomainCode ?? "company"}");

        await SendAsync(request, sender, userAgent, cancellationToken);
        logger.LogInformation("Message to the developer sent by user {UserId}.", user.Id);
    }

    public async Task SendFromClientAsync(ReportBugRequest request, string? userAgent, CancellationToken cancellationToken = default)
    {
        var account = await portalAccounts.GetByIdAsync(currentPortalAccount.Id, cancellationToken)
            ?? throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "You need to sign in.");
        var name = $"{account.FirstName} {account.LastName}".Trim();
        var sender = new ContactSender(name.Length > 0 ? name : account.Email, account.Email, $"portal account {account.Id}", "client, in the portal");

        await SendAsync(request, sender, userAgent, cancellationToken);
        logger.LogInformation("Message to the developer sent by portal account {PortalAccountId}.", account.Id);
    }

    private async Task SendAsync(ReportBugRequest request, ContactSender sender, string? userAgent, CancellationToken cancellationToken)
    {
        var report = new BugReport(
            request.Description.Trim(),
            string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
            request.Page,
            sender,
            OneLine(userAgent),
            request.InstalledApp,
            Stamp(clock.UtcNow),
            request.Error is { } error ? new ContactError(error.Ref, Stamp(error.At.ToUniversalTime()), error.Message?.Trim()) : null,
            request.RecentErrors.Select(e => new ReportedError(e.Ref, e.Status, e.Path, e.At.ToUniversalTime())).ToList());
        var productName = emailOptions.Value.ProductName;

        try
        {
            await emailSender.SendAsync(SupportEmails.BugReport(productName, options.Value.ToAddress, report), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not email the message to the developer from {Account}.", sender.Account);
            throw new ServiceUnavailableException(ErrorCodes.EmailDeliveryFailed, "Your message couldn't be sent. Please try again in a few minutes, or use WhatsApp.", ex);
        }

        // the message has gone: a copy that fails to send is only logged
        try
        {
            await emailSender.SendAsync(SupportEmails.BugReportCopy(productName, report), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not email {Account} the copy of their message to the developer.", sender.Account);
        }
    }

    private StampedTime Stamp(DateTime utc) => new(utc, clock.ToCompanyTime(utc));

    private static string? OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > MaxUserAgentLength ? clean[..MaxUserAgentLength] : clean;
    }
}
