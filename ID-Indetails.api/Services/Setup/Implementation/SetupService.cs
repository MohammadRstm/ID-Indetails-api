using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Setup;
using ID_Indetails.api.DTO.Response.Setup;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Setup.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Setup.Implementation;

public sealed class SetupService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    IEmailSender emailSender,
    IClock clock,
    IOptions<FrontendOptions> frontendOptions,
    IOptions<EmailOptions> emailOptions,
    ILogger<SetupService> logger) : ISetupService
{
    public async Task<SuperAdminCreatedResponse> CreateSuperAdminAsync(
        CreateSuperAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = EmailAddress.Normalize(request.Email);
        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.EmailAlreadyRegistered, "Someone with this email is already registered.");
        }

        var now = clock.UtcNow;
        var temporaryPassword = PasswordGenerator.Generate();
        var user = new User
        {
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(),
            RoleCode = RoleCodes.SuperAdmin,
            DomainCode = null,
            Status = UserStatus.Active,
            MustChangePassword = true,
            JoinedOn = clock.CompanyToday,
            CreatedAt = now,
        };
        user.PasswordHash = passwords.Hash(user, temporaryPassword);
        user.EmploymentPeriods.Add(new EmploymentPeriod { StartedOn = clock.CompanyToday, CreatedAt = now });

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(
                AccountEmails.SuperAdminCredentials(
                    emailOptions.Value.ProductName,
                    user.Email,
                    user.FirstName,
                    temporaryPassword,
                    frontendOptions.Value.LoginUrl),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send the super admin credentials email.");
            throw new ServiceUnavailableException(
                ErrorCodes.EmailDeliveryFailed,
                "The credentials email could not be sent, so the account was not created. Please try again.",
                ex);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Super admin {UserId} created through the setup endpoint.", user.Id);

        return new SuperAdminCreatedResponse(user.Id, user.Email);
    }
}
