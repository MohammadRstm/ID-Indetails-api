using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Reusable steps for tests that need clients and projects.</summary>
public sealed class ProjectTestSteps(AuthTestSteps steps)
{
    private HttpClient Client => steps.Client;

    public async Task<ClientResponse> CreateClientAsync(string accessToken, string domain)
    {
        var response = await Client.PostJsonAsync("/api/clients", new
        {
            name = "Client " + Guid.NewGuid().ToString("N")[..8],
            label = "Client",
            domainCode = domain,
            contacts = new[] { new { kind = "email", value = AuthTestSteps.NewEmail("client") } },
        }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ClientResponse>();
    }

    /// <summary>
    /// A Design client project with the given revenue, or an Execution site with a work price of 1000, a 10% markup and no
    /// supervision, overseen by <paramref name="staffIds"/> (by default someone made for it). A caller who may not set that
    /// money (a plain admin) gets the project without it: no revenue, or no work price and no markup.
    /// </summary>
    /// <param name="projectFee">
    /// An Execution site's work price; 1,000 unless said. It is never typed in (agreeing a BOQ sets it), so the test sets
    /// it in the database as a fast path (<see cref="SetWorkPriceAsync"/>). A test of the real flow agrees a BOQ instead
    /// (<see cref="BoqTestSteps.AgreeClientBoqAsync"/>, or <see cref="AgreeWorkPriceAsync"/>), with <c>projectFee: 0</c> here.
    /// </param>
    public async Task<ProjectResponse> CreateProjectAsync(
        string accessToken, string domain, decimal revenue = 5000, IReadOnlyList<long>? staffIds = null, decimal projectFee = 1000m)
    {
        var client = await CreateClientAsync(accessToken, domain);
        var me = await (await Client.GetAsync("/api/auth/me", accessToken)).ReadAsync<CurrentUserResponse>();
        var title = (domain == DomainCodes.Execution ? "Site " : "Project ") + Guid.NewGuid().ToString("N")[..8];
        var staff = domain == DomainCodes.Execution ? staffIds ?? [await SiteStaffAsync()] : null;
        // untaxed, so money figures read as before TVA; TVA has tests of its own
        object body = domain == DomainCodes.Execution
            ? me.Permissions.Contains(PermissionCodes.ProjectPricingManage)
                ? new { title, domainCode = domain, clientId = client.Id, materialMarkup = 0.10m, supervisionRate = 0m, staffIds = staff, tvaRate = 0m }
                : new { title, domainCode = domain, clientId = client.Id, staffIds = staff }
            : me.Permissions.Contains(PermissionCodes.FinancialView)
                ? new { title, domainCode = domain, clientId = client.Id, revenue, tvaRate = 0m }
                : new { title, domainCode = domain, clientId = client.Id };

        var response = await Client.PostJsonAsync("/api/projects", body, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        var project = await response.ReadAsync<ProjectResponse>();
        if (domain != DomainCodes.Execution || !me.Permissions.Contains(PermissionCodes.ProjectPricingManage) || projectFee <= 0)
        {
            return project;
        }

        await SetWorkPriceAsync(project.Id, projectFee);
        return await (await Client.GetAsync($"/api/projects/{project.Id}", accessToken)).ReadAsync<ProjectResponse>();
    }

    /// <summary>
    /// Sets an Execution site's work price in the database, with the <c>project_fee_set</c> event (new and previous
    /// amount) that agreeing a BOQ writes. The fast path: it skips the BOQ and the profit floor.
    /// </summary>
    public Task SetWorkPriceAsync(long projectId, decimal fee)
        => steps.ExecuteAsync(
            "INSERT INTO activity_events (domain_code, kind, occurred_at, project_id, subject_type, subject_id, amount, prev_amount, is_demo) " +
            "SELECT 'execution', 'project_fee_set', UTC_TIMESTAMP(3), id, 'project', id, @fee, project_fee, 0 FROM projects WHERE id = @id; " +
            "UPDATE projects SET project_fee = @fee WHERE id = @id",
            ("@id", projectId), ("@fee", fee));

    /// <summary>Gives a site its work price the real way: a client BOQ of one line worth <paramref name="work"/>, agreed.</summary>
    public Task AgreeWorkPriceAsync(string accessToken, long projectId, decimal work)
        => new BoqTestSteps(steps).AgreeClientBoqAsync(accessToken, projectId, work);

    /// <summary>An active Execution employee made straight in the database, to oversee a site no test signs in to.</summary>
    public Task<long> SiteStaffAsync()
        => steps.ScalarAsync<long>(
            "INSERT INTO users (email, first_name, last_name, password_hash, role_code, domain_code, status, must_change_password, created_at) " +
            "VALUES (@e, 'Site', 'Staff', 'not-a-real-hash', 'employee', 'execution', 'active', 0, UTC_TIMESTAMP(3)); SELECT LAST_INSERT_ID()",
            ("@e", AuthTestSteps.NewEmail("site-staff")));

    /// <summary>Adds people to the staff overseeing a site, keeping those already on it.</summary>
    public async Task PutOnSiteAsync(string accessToken, long projectId, params long[] userIds)
    {
        var site = await (await Client.GetAsync($"/api/projects/{projectId}", accessToken)).ReadAsync<ProjectResponse>();
        var staffIds = (site.Staff ?? []).Select(s => s.Id).Union(userIds).ToList();
        var response = await Client.PutJsonAsync($"/api/projects/{projectId}/staff", new { staffIds }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Billing settings with 45-day terms, prefix HL for Design and EX for Execution.</summary>
    public async Task SetUpBillingAsync(string accessToken, string domain)
    {
        var response = await Client.PutJsonAsync($"/api/billing-settings/{domain}", new
        {
            invoicePrefix = domain == DomainCodes.Execution ? "EX" : "HL",
            defaultTermsDays = 45,
        }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
    }
}
