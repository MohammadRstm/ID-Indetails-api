using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.DevSignIn;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.DevSignIn.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.DevSignIn.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;

namespace ID_Indetails.api.Services.DevSignIn.Implementation;

public sealed class DevSignInService(
    IDevAccountRepository accounts,
    IUserRepository users,
    IPortalAccountRepository portalAccounts,
    ISessionService sessions,
    IPortalSessionService portalSessions,
    IUnitOfWork unitOfWork,
    ILoggerFactory loggers) : IDevSignInService
{
    private const string ClientsLabel = "Clients (portal)";

    // the order the groups show in: Design, then Execution, then Website, then anything else
    private static readonly string[] DomainOrder = [DomainCodes.Design, DomainCodes.Execution, DomainCodes.Website];

    private readonly ILogger _security = loggers.CreateLogger(SecurityLog.Category);

    public async Task<DevAccountsResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var staff = await accounts.SignableStaffAsync(cancellationToken);
        var clients = await accounts.ActivePortalAccountsAsync(cancellationToken);

        var groups = staff
            .GroupBy(s => (Order: GroupOrder(s), Label: GroupLabel(s)))
            .OrderBy(g => g.Key.Order)
            .Select(g => new DevAccountGroupResponse
            {
                Label = g.Key.Label,
                Accounts = g
                    .Select(s => new DevAccountResponse
                    {
                        Kind = DevAccountKind.Staff,
                        Id = s.Id,
                        Name = NameOf(s.FirstName, s.LastName, s.Email),
                        Email = s.Email,
                        Detail = s.Position ?? s.TeamName,
                    })
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();

        if (clients.Count > 0)
        {
            groups.Add(new DevAccountGroupResponse
            {
                Label = ClientsLabel,
                Accounts = clients
                    .Select(c => new DevAccountResponse
                    {
                        Kind = DevAccountKind.Client,
                        Id = c.Id,
                        Name = NameOf(c.FirstName, c.LastName, c.Email),
                        Email = c.Email,
                    })
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            });
        }

        return new DevAccountsResponse { Groups = groups };
    }

    public async Task<AuthSession> SignInAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is not { Status: UserStatus.Active, SessionsRevokedAt: null })
        {
            throw new NotFoundException("No staff member who can sign in has that id.");
        }

        var session = await sessions.StartAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _security.LogWarning("Development sign-in as user {UserId}, without a password.", user.Id);
        return session;
    }

    public async Task<PortalAuthSession> PortalSignInAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var account = await portalAccounts.GetByIdAsync(accountId, cancellationToken);
        if (account is not { Status: PortalAccountStatus.Active })
        {
            throw new NotFoundException("No active portal account has that id.");
        }

        var session = await portalSessions.StartAsync(account, Guid.NewGuid(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _security.LogWarning("Development sign-in as portal account {AccountId}, without a password.", account.Id);
        return session;
    }

    /// <summary>Super admins first; then each domain's admins before its employees.</summary>
    private static int GroupOrder(DevStaffAccount s)
    {
        if (s.RoleCode == RoleCodes.SuperAdmin)
        {
            return 0;
        }

        var domain = s.DomainCode is null ? -1 : Array.IndexOf(DomainOrder, s.DomainCode);
        var role = s.RoleCode == RoleCodes.Admin ? 0 : 1;
        return 1 + (domain < 0 ? DomainOrder.Length : domain) * 2 + role;
    }

    private static string GroupLabel(DevStaffAccount s)
    {
        if (s.RoleCode == RoleCodes.SuperAdmin)
        {
            return "Super admins";
        }

        var domain = s.DomainCode switch
        {
            DomainCodes.Design => "Design",
            DomainCodes.Execution => "Execution",
            DomainCodes.Website => "Website",
            _ => "Company",
        };
        return $"{domain} {(s.RoleCode == RoleCodes.Admin ? "admins" : "employees")}";
    }

    private static string NameOf(string? firstName, string? lastName, string email)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length > 0 ? name : email;
    }
}
