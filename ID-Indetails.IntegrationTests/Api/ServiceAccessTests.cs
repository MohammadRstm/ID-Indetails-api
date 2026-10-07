using System.Security.Claims;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A service checks the permission its endpoint's attribute asks for too, so a caller that isn't that endpoint (a job,
/// a hub, another service) can't reach what the person may not see.
/// </summary>
public class ServiceAccessTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);

    /// <summary>A request scope signed in as <paramref name="userId"/>, as a hub or a new endpoint would run.</summary>
    private AsyncServiceScope SignedInAs(long userId)
    {
        var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaimTypes.UserId, userId.ToString())], "test")),
        };
        return scope;
    }

    [Fact]
    public async Task Services_refuse_an_employee_what_their_endpoints_would()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await using var scope = SignedInAs(employee.User.Id);
        var services = scope.ServiceProvider;
        await Should.ThrowAsync<ForbiddenException>(() => services.GetRequiredService<ICompanyOverviewService>().GetAsync(null, null));
        await Should.ThrowAsync<ForbiddenException>(() => services.GetRequiredService<ICompanyOverviewService>().MonthlyAsync(3));
        await Should.ThrowAsync<ForbiddenException>(() => services.GetRequiredService<IProjectService>().TotalsAsync(new ProjectListQuery { Domain = DomainCodes.Design }));
        await Should.ThrowAsync<ForbiddenException>(() => services.GetRequiredService<ISalaryService>().ListAsync(new SalaryListQuery()));
        await Should.ThrowAsync<ForbiddenException>(() => services.GetRequiredService<IWorkEntryQueryService>().ListAsync(new WorkEntryListQuery()));

        // a super admin holds every one of them
        await using var allowed = SignedInAs(superAdmin.User.Id);
        (await allowed.ServiceProvider.GetRequiredService<ICompanyOverviewService>().GetAsync(null, null)).ShouldNotBeNull();
    }
}
