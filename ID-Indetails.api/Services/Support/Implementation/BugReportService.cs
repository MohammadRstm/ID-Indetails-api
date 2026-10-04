using ID_Indetails.api.Authorization;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Support;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Support.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Support.Implementation;

/// <summary>Bug reports go by email to <see cref="BugReportOptions.ToAddress"/>; nothing is stored.</summary>
public sealed class BugReportService(
    ICurrentUser currentUser,
    IUserRepository users,
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
        var report = new BugReport(
            request.Title.Trim(),
            request.Description.Trim(),
            string.IsNullOrWhiteSpace(request.Steps) ? null : request.Steps.Trim(),
            request.Page,
            user.Id,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Email,
            user.RoleCode,
            user.DomainCode,
            OneLine(userAgent),
            clock.UtcNow);

        try
        {
            await emailSender.SendAsync(SupportEmails.BugReport(emailOptions.Value.ProductName, options.Value.ToAddress, report), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not email the bug report from user {UserId}.", user.Id);
            throw new ServiceUnavailableException(ErrorCodes.EmailDeliveryFailed, "Your report couldn't be sent. Please try again in a few minutes.", ex);
        }

        logger.LogInformation("Bug report sent by user {UserId}.", user.Id);
    }

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
