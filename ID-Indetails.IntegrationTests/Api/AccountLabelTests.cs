using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Company;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Account labels: one company-wide list, read by every signed-in person and edited by super admins.</summary>
public class AccountLabelTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static string NewName() => "Label " + Guid.NewGuid().ToString("N")[..8];

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private Task<List<AccountLabelResponse>> ListAsync(string token, bool includeArchived = false)
        => OkAsync<List<AccountLabelResponse>>(Client.GetAsync($"/api/account-labels?includeArchived={includeArchived.ToString().ToLowerInvariant()}", token));

    private Task<AccountLabelResponse> AddAsync(string token, string name)
        => OkAsync<AccountLabelResponse>(Client.PostJsonAsync("/api/account-labels", new { name }, token));

    [Fact]
    public async Task The_list_starts_with_the_three_seeded_labels_in_order_for_any_signed_in_person()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        var labels = await ListAsync(employee.AccessToken);

        labels.Select(l => l.Name).Take(3).ShouldBe(["Concrete", "Finishing", "Permits"]);
        labels.Select(l => l.Position).ShouldBe(labels.Select(l => l.Position).Order());
        labels.ShouldAllBe(l => !l.Archived);
        await (await Client.GetAsync("/api/account-labels")).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task A_super_admin_adds_renames_archives_and_restores_a_label()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = NewName();

        var added = await AddAsync(superAdmin.AccessToken, $"  {name}  ");
        added.ShouldSatisfyAllConditions(l => l.Name.ShouldBe(name), l => l.Archived.ShouldBeFalse());
        (await ListAsync(superAdmin.AccessToken)).Last().Id.ShouldBe(added.Id);

        var renamed = await OkAsync<AccountLabelResponse>(Client.PutJsonAsync($"/api/account-labels/{added.Id}", new { name = name + " II" }, superAdmin.AccessToken));
        renamed.Name.ShouldBe(name + " II");

        var archived = await OkAsync<AccountLabelResponse>(Client.PostEmptyAsync($"/api/account-labels/{added.Id}/archive", superAdmin.AccessToken));
        archived.Archived.ShouldBeTrue();
        (await ListAsync(superAdmin.AccessToken)).ShouldNotContain(l => l.Id == added.Id);
        (await ListAsync(superAdmin.AccessToken, includeArchived: true)).ShouldContain(l => l.Id == added.Id && l.Archived);

        // a restored label comes back last, after any added meanwhile
        var other = await AddAsync(superAdmin.AccessToken, NewName());
        var restored = await OkAsync<AccountLabelResponse>(Client.PostEmptyAsync($"/api/account-labels/{added.Id}/restore", superAdmin.AccessToken));
        restored.Archived.ShouldBeFalse();
        restored.Position.ShouldBeGreaterThan(other.Position);

        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'account_label_changed' AND subject_id = @id", ("@id", added.Id))).ShouldBe(4);
    }

    [Fact]
    public async Task Names_are_trimmed_unique_whatever_their_case_and_short()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var name = NewName();
        var added = await AddAsync(superAdmin.AccessToken, name);

        await (await Client.PostJsonAsync("/api/account-labels", new { name = name.ToUpperInvariant() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.AccountLabelTaken);
        await (await Client.PostJsonAsync("/api/account-labels", new { name = $" {name.ToLowerInvariant()} " }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.AccountLabelTaken);
        await (await Client.PutJsonAsync($"/api/account-labels/{added.Id}", new { name = "concrete" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.AccountLabelTaken);

        // an archived label keeps its name taken
        await Client.PostEmptyAsync($"/api/account-labels/{added.Id}/archive", superAdmin.AccessToken);
        await (await Client.PostJsonAsync("/api/account-labels", new { name }, superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.AccountLabelTaken);

        // its own name in another case is a rename, not a clash
        (await OkAsync<AccountLabelResponse>(Client.PutJsonAsync($"/api/account-labels/{added.Id}", new { name = name.ToUpperInvariant() }, superAdmin.AccessToken)))
            .Name.ShouldBe(name.ToUpperInvariant());

        (await (await Client.PostJsonAsync("/api/account-labels", new { name = "   " }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("name");
        (await (await Client.PostJsonAsync("/api/account-labels", new { name = new string('x', 41) }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!.Keys.ShouldContain("name");
        await (await Client.PutJsonAsync("/api/account-labels/999999999", new { name = NewName() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Only_super_admins_edit_the_list_and_see_the_archived_ones()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var label = await AddAsync(superAdmin.AccessToken, NewName());

        foreach (var token in new[] { admin.AccessToken, employee.AccessToken })
        {
            await (await Client.PostJsonAsync("/api/account-labels", new { name = NewName() }, token)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PutJsonAsync($"/api/account-labels/{label.Id}", new { name = NewName() }, token)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostEmptyAsync($"/api/account-labels/{label.Id}/archive", token)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PostEmptyAsync($"/api/account-labels/{label.Id}/restore", token)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.PutJsonAsync("/api/account-labels/order", new { ids = new[] { label.Id } }, token)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await Client.GetAsync("/api/account-labels?includeArchived=true", token)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }
    }

    [Fact]
    public async Task The_order_lists_every_label_that_can_be_picked_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await AddAsync(superAdmin.AccessToken, NewName());
        var second = await AddAsync(superAdmin.AccessToken, NewName());
        var archived = await AddAsync(superAdmin.AccessToken, NewName());
        await Client.PostEmptyAsync($"/api/account-labels/{archived.Id}/archive", superAdmin.AccessToken);

        // the second goes to the front
        var active = (await ListAsync(superAdmin.AccessToken)).Select(l => l.Id).ToList();
        var reordered = active.Where(id => id != second.Id).Prepend(second.Id).ToList();
        var saved = await OkAsync<List<AccountLabelResponse>>(Client.PutJsonAsync("/api/account-labels/order", new { ids = reordered }, superAdmin.AccessToken));
        saved.Select(l => l.Id).Where(reordered.Contains).ShouldBe(reordered);
        saved.ShouldContain(l => l.Id == archived.Id && l.Archived);
        (await ListAsync(superAdmin.AccessToken)).Select(l => l.Id).ShouldBe(reordered);

        // one left out, one twice, an archived one and an unknown one are all refused, and nothing is saved
        var refused = new[]
        {
            reordered.Skip(1).ToList(),
            reordered.Append(reordered[0]).ToList(),
            reordered.Append(archived.Id).ToList(),
            reordered.Append(0L).ToList(),
        };
        foreach (var ids in refused)
        {
            await (await Client.PutJsonAsync("/api/account-labels/order", new { ids }, superAdmin.AccessToken))
                .ShouldFailWithAsync(422, ErrorCodes.AccountLabelOrderStale);
        }

        (await ListAsync(superAdmin.AccessToken)).Select(l => l.Id).ShouldBe(reordered);
    }
}
