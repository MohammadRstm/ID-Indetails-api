using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ClientTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static object NewClient(string? domainCode = null, string? email = null) => new
    {
        name = "Rami Khoury",
        organisation = $"Cedar {Guid.NewGuid():N}"[..20],
        label = "Client",
        domainCode,
        contacts = new[]
        {
            new { kind = "email", value = email ?? AuthTestSteps.NewEmail("client"), isWhatsapp = false },
            new { kind = "phone", value = $"+961 {Random.Shared.NextInt64(10_000_000, 99_999_999)}", isWhatsapp = true },
        },
    };

    [Fact]
    public async Task Admin_adds_edits_archives_restores_and_deletes_a_client()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        var created = await Client.PostJsonAsync("/api/clients", NewClient(), admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var client = await created.ReadAsync<ClientResponse>();
        client.Domain.ShouldBe(DomainCodes.Design);
        client.Contacts.Count.ShouldBe(2);
        client.Contacts[0].IsPrimary.ShouldBeTrue();

        var email = AuthTestSteps.NewEmail("renamed");
        var updated = await (await Client.PutJsonAsync($"/api/clients/{client.Id}", new
        {
            name = "Rami K.",
            label = "Key account",
            note = "Prefers WhatsApp",
            contacts = new[] { new { kind = "email", value = email, isPrimary = true } },
        }, admin.AccessToken)).ReadAsync<ClientResponse>();
        updated.DisplayName.ShouldBe("Rami K.");
        updated.Contacts.Single().Value.ShouldBe(email);

        (await (await Client.PostEmptyAsync($"/api/clients/{client.Id}/archive", admin.AccessToken)).ReadAsync<ClientResponse>())
            .IsArchived.ShouldBeTrue();
        await (await Client.PostEmptyAsync($"/api/clients/{client.Id}/archive", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ClientAlreadyArchived);

        var archived = await (await Client.GetAsync("/api/clients?archived=true", admin.AccessToken))
            .ReadAsync<PagedResponse<ClientSummaryResponse>>();
        archived.Items.ShouldContain(c => c.Id == client.Id);

        (await (await Client.PostEmptyAsync($"/api/clients/{client.Id}/restore", admin.AccessToken)).ReadAsync<ClientResponse>())
            .IsArchived.ShouldBeFalse();

        (await Client.DeleteAsync($"/api/clients/{client.Id}", admin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        await (await Client.GetAsync($"/api/clients/{client.Id}", admin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE subject_type = 'client' AND subject_id = @id", ("@id", client.Id)))
            .ShouldBe(5);
    }

    [Fact]
    public async Task A_contact_belongs_to_one_client_per_domain_and_a_shared_name_only_warns()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var email = AuthTestSteps.NewEmail("shared");

        var first = await (await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Design, email), superAdmin.AccessToken))
            .ReadAsync<ClientResponse>();
        first.NameSharedWithAnotherClient.ShouldBeFalse();

        await (await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Design, email.ToUpperInvariant()), superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.ContactTaken);

        // Archived clients still own their contacts.
        await Client.PostEmptyAsync($"/api/clients/{first.Id}/archive", superAdmin.AccessToken);
        await (await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Design, email), superAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.ContactTaken);

        // The other domain is a separate business.
        var other = await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Execution, email), superAdmin.AccessToken);
        ((int)other.StatusCode).ShouldBe(201);

        var twin = await (await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Design), superAdmin.AccessToken))
            .ReadAsync<ClientResponse>();
        twin.NameSharedWithAnotherClient.ShouldBeTrue();
    }

    [Fact]
    public async Task Clients_of_another_domain_read_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var execution = await (await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Execution), superAdmin.AccessToken))
            .ReadAsync<ClientResponse>();

        await (await Client.GetAsync($"/api/clients/{execution.Id}", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PutJsonAsync($"/api/clients/{execution.Id}", NewClient(), designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"/api/clients/{execution.Id}", designAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync("/api/clients", NewClient(DomainCodes.Execution), designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        var list = await (await Client.GetAsync("/api/clients?pageSize=100", designAdmin.AccessToken))
            .ReadAsync<PagedResponse<ClientSummaryResponse>>();
        list.Items.ShouldAllBe(c => c.Domain == DomainCodes.Design);
    }

    [Fact]
    public async Task Employees_cannot_see_or_manage_clients_and_bad_input_is_rejected()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.GetAsync("/api/clients", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/clients", NewClient(), employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await Client.GetAsync("/api/clients", "not-a-token")).StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);

        // A super admin sees both domains, so must say which one.
        await (await Client.PostJsonAsync("/api/clients", NewClient(), superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync("/api/clients", new { name = "No contacts", label = "Client", domainCode = DomainCodes.Design, contacts = Array.Empty<object>() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
