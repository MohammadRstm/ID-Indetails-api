using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Activity.Implementation;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Auth.Implementation;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using ID_Indetails.api.Repositories.Clients.Implementation;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Common.Implementation;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using ID_Indetails.api.Repositories.Employees.Implementation;
using ID_Indetails.api.Repositories.Invitations.Abstraction;
using ID_Indetails.api.Repositories.Invitations.Implementation;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Projects.Implementation;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Implementation;
using ID_Indetails.api.Repositories.Teams.Abstraction;
using ID_Indetails.api.Repositories.Teams.Implementation;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Implementation;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Clients.Implementation;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Email.Abstraction;
using ID_Indetails.api.Services.Email.Implementation;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Employees.Implementation;
using ID_Indetails.api.Services.Invitations.Abstraction;
using ID_Indetails.api.Services.Invitations.Implementation;
using ID_Indetails.api.Services.Permissions.Abstraction;
using ID_Indetails.api.Services.Permissions.Implementation;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Projects.Implementation;
using ID_Indetails.api.Services.Requirements.Abstraction;
using ID_Indetails.api.Services.Requirements.Implementation;
using ID_Indetails.api.Services.Setup.Abstraction;
using ID_Indetails.api.Services.Setup.Implementation;
using ID_Indetails.api.Services.Teams.Abstraction;
using ID_Indetails.api.Services.Teams.Implementation;

namespace ID_Indetails.api.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Repositories, services and their settings.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<CompanyOptions>().BindConfiguration(CompanyOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Company:TimeZone is not a known time zone.")
            .ValidateOnStart();
        services.AddOptions<InvitationOptions>().BindConfiguration(InvitationOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<FrontendOptions>().BindConfiguration(FrontendOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();

        // Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IActivityEventRepository, ActivityEventRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IRequirementRepository, RequirementRepository>();

        // Services
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IPermissionManagementService, PermissionManagementService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectProgress, ProjectProgress>();
        services.AddScoped<IProjectStatusService, ProjectStatusService>();
        services.AddScoped<IRequirementService, RequirementService>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
